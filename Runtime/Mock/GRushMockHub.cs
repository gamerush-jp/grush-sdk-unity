using System;
using System.Collections.Generic;
using System.Text;

namespace GRushSdk
{
    internal sealed class GRushMockMember
    {
        public int Index;
        public string PseudoId;
        public string DisplayName;
        public string AvatarUrl;
        public GRushMockPeer Peer;
        public Action<GRushNetEvent> Deliver;
    }

    internal sealed class GRushMockHub
    {
        private static GRushMockHub instance;

        private readonly List<GRushMockMember> members = new List<GRushMockMember>();
        private readonly Dictionary<int, uint> outgoingSeq = new Dictionary<int, uint>();
        private readonly Random random = new Random(20260816);
        private string roomCode;
        private int roomSerial = 1;

        // 今の部屋の定員と形。configured はゲームが join で設定を決めたか。
        private int maxPeers = GRushMockNetParams.DefaultMaxPeers;
        private GRushTopology topology = GRushTopology.Mesh;
        private bool configured;

        public static GRushMockHub Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new GRushMockHub();
                }
                return instance;
            }
        }

        public static void Reset()
        {
            instance = null;
        }

        /// <summary>
        /// 部屋へ入る。部屋が埋まっていれば null を返す。部屋コードで指定した部屋が違う定員か形で
        /// 既にあれば null を返し、mismatch を true にする。
        /// </summary>
        public GRushJoinWire Join(
            string mode,
            string requestedCode,
            int targetRoomSize,
            int requestedMaxPeers,
            GRushTopology requestedTopology,
            string pseudoId,
            string displayName,
            string avatarUrl,
            Action<GRushNetEvent> deliver,
            out bool mismatch
        )
        {
            mismatch = false;
            var sameConfig = requestedMaxPeers == maxPeers && requestedTopology == topology;
            if (!string.IsNullOrEmpty(requestedCode))
            {
                // GameRush の部屋の設定は最初に入った人の join で決まり、後から変わらない。
                if (configured && !sameConfig && requestedCode == roomCode)
                {
                    mismatch = true;
                    return null;
                }
                if (configured && !sameConfig)
                {
                    StartNextRoom();
                }
            }
            else if (configured && !sameConfig)
            {
                // GameRush は同じ定員と形の部屋だけから選ぶので、違う設定の join は別の部屋へ入る。
                StartNextRoom();
            }
            else if (targetRoomSize > 0 && members.Count >= Math.Min(targetRoomSize, requestedMaxPeers))
            {
                StartNextRoom();
            }
            if (!configured)
            {
                // AddPeer の相手しかいない部屋は、ゲームの最初の join で設定が決まる。
                if (!FitsCapacity(requestedMaxPeers))
                {
                    return null;
                }
                maxPeers = requestedMaxPeers;
                topology = requestedTopology;
                configured = true;
            }
            var index = NextFreeIndex();
            if (index < 0)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(requestedCode))
            {
                roomCode = requestedCode;
            }
            else if (string.IsNullOrEmpty(roomCode))
            {
                roomCode = "MOCKROOM";
            }
            var local = new GRushMockMember
            {
                Index = index,
                PseudoId = pseudoId,
                DisplayName = displayName,
                AvatarUrl = avatarUrl,
                Deliver = deliver,
            };
            var existing = new List<GRushPeerWire>();
            foreach (var member in members)
            {
                existing.Add(WireOf(member));
            }
            members.Add(local);
            members.Sort((a, b) => a.Index.CompareTo(b.Index));
            Announce(local, GRushNetEventKind.PeerJoin);
            return new GRushJoinWire
            {
                roomId = "mock:" + mode + ":" + roomCode,
                roomCode = roomCode,
                localPeerIndex = local.Index,
                peers = existing.ToArray(),
                hostIndex = HostIndex(),
                epoch = 1,
                transport = "ws",
                serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                maxPeers = maxPeers,
                topology = GRushWire.TopologyName(topology),
            };
        }

        public GRushMockPeer AddPeer(string displayName, string avatarUrl)
        {
            var index = NextFreeIndex();
            if (index < 0)
            {
                return null;
            }
            var peer = new GRushMockPeer
            {
                Index = index,
                PseudoId = "mock-peer-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                DisplayName = displayName,
                AvatarUrl = avatarUrl,
            };
            var member = new GRushMockMember
            {
                Index = peer.Index,
                PseudoId = peer.PseudoId,
                DisplayName = peer.DisplayName,
                AvatarUrl = peer.AvatarUrl,
                Peer = peer,
            };
            members.Add(member);
            members.Sort((a, b) => a.Index.CompareTo(b.Index));
            Announce(member, GRushNetEventKind.PeerJoin);
            return peer;
        }

        public void RemovePeer(GRushMockPeer peer)
        {
            var member = members.Find(candidate => candidate.Peer == peer);
            if (member == null)
            {
                return;
            }
            members.Remove(member);
            outgoingSeq.Remove(member.Index);
            Announce(member, GRushNetEventKind.PeerLeave);
            ForgetConfigIfEmpty();
        }

        public void Leave(int index)
        {
            var member = members.Find(candidate => candidate.Index == index);
            if (member == null)
            {
                return;
            }
            members.Remove(member);
            outgoingSeq.Remove(index);
            Announce(member, GRushNetEventKind.PeerLeave);
            ForgetConfigIfEmpty();
        }

        public bool RaiseError(string code, string message)
        {
            var detail = ErrorJson(code, message);
            var delivered = false;
            foreach (var member in members.ToArray())
            {
                if (member.Peer != null || member.Deliver == null)
                {
                    continue;
                }
                PostError(member.Deliver, detail);
                delivered = true;
            }
            return delivered;
        }

        /// <summary>
        /// 部屋に残っている相手の送信だけを通す。外された相手は前の部屋に残った扱いになる。
        /// hub の部屋で宛先が許されなければ送らずに false を返す。
        /// </summary>
        public bool RouteFromPeer(GRushMockPeer peer, byte[] payload, GRushChannel channel, int to)
        {
            if (!members.Exists(candidate => candidate.Peer == peer))
            {
                return true;
            }
            if (!IsAllowedTarget(peer.Index, to))
            {
                return false;
            }
            Route(peer.Index, payload, payload.Length, channel, to);
            return true;
        }

        /// <summary>
        /// ゲームの送信を通す。hub の部屋で宛先が許されなければ、GameRush と同じく送らずに
        /// targetNotAllowed の error を届ける。
        /// </summary>
        public void RouteFromLocal(int from, byte[] payload, int count, GRushChannel channel, int to)
        {
            if (!IsAllowedTarget(from, to))
            {
                var sender = members.Find(candidate => candidate.Index == from);
                if (sender != null && sender.Deliver != null)
                {
                    PostError(sender.Deliver, "targetNotAllowed", "Send to the host only.");
                }
                return;
            }
            Route(from, payload, count, channel, to);
        }

        // hub の非ホストが送れるのはホスト宛てと全員宛てだけ。
        private bool IsAllowedTarget(int from, int to)
        {
            if (topology != GRushTopology.Hub || from == HostIndex())
            {
                return true;
            }
            return to < 0 || to == HostIndex();
        }

        private void Route(int from, byte[] payload, int count, GRushChannel channel, int to)
        {
            // hub の非ホストの全員宛ては、GameRush と同じくホストにだけ届く。
            if (topology == GRushTopology.Hub && from != HostIndex())
            {
                to = HostIndex();
            }
            if (channel == GRushChannel.Unreliable && random.NextDouble() < GRushMock.UnreliableDropRate)
            {
                return;
            }
            uint seq;
            outgoingSeq.TryGetValue(from, out seq);
            seq += 1;
            outgoingSeq[from] = seq;
            foreach (var member in members.ToArray())
            {
                if (member.Index == from || (to != GRushRoom.Everyone && member.Index != to))
                {
                    continue;
                }
                var copy = new byte[count];
                Buffer.BlockCopy(payload, 0, copy, 0, count);
                DeliverMessage(member, from, copy, channel, seq);
            }
        }

        private static string ErrorJson(string code, string message)
        {
            return "{\"code\":"
                + GRushWire.Escape(code ?? string.Empty)
                + ",\"message\":"
                + GRushWire.Escape(message ?? string.Empty)
                + "}";
        }

        private static void PostError(Action<GRushNetEvent> deliver, string code, string message)
        {
            PostError(deliver, ErrorJson(code, message));
        }

        private static void PostError(Action<GRushNetEvent> deliver, string detail)
        {
            GRushDispatcher.Post(
                () => deliver(new GRushNetEvent { Kind = GRushNetEventKind.Error, Detail = detail })
            );
        }

        private void DeliverMessage(
            GRushMockMember member,
            int from,
            byte[] payload,
            GRushChannel channel,
            uint seq
        )
        {
            if (member.Peer != null)
            {
                var peer = member.Peer;
                GRushDispatcher.Post(
                    () =>
                        peer.Deliver(
                            new GRushMessage
                            {
                                From = from,
                                Channel = channel,
                                Seq = seq,
                                Payload = payload,
                            }
                        )
                );
                return;
            }
            var deliver = member.Deliver;
            GRushDispatcher.Post(
                () =>
                    deliver(
                        new GRushNetEvent
                        {
                            Kind = GRushNetEventKind.Message,
                            From = from,
                            Channel = channel,
                            Seq = seq,
                            Payload = payload,
                        }
                    )
            );
        }

        private void Announce(GRushMockMember subject, GRushNetEventKind kind)
        {
            var detail =
                kind == GRushNetEventKind.PeerJoin ? BuildPeerJson(subject) : string.Empty;
            foreach (var member in members.ToArray())
            {
                if (member.Deliver == null || member.Index == subject.Index)
                {
                    continue;
                }
                var deliver = member.Deliver;
                GRushDispatcher.Post(
                    () =>
                        deliver(
                            new GRushNetEvent { Kind = kind, From = subject.Index, Detail = detail }
                        )
                );
            }
        }

        // モックの部屋は1つなので、部屋が目安に届いていれば、どの部屋も目安以上だったことにする。
        // ゲームは新しい部屋へ移り、AddPeer で作った相手は前の部屋に残る。
        private void StartNextRoom()
        {
            members.Clear();
            outgoingSeq.Clear();
            roomSerial += 1;
            roomCode = "MOCKROOM" + roomSerial;
            maxPeers = GRushMockNetParams.DefaultMaxPeers;
            topology = GRushTopology.Mesh;
            configured = false;
        }

        // GameRush の部屋は無人になると設定を忘れる。
        private void ForgetConfigIfEmpty()
        {
            if (members.Count == 0)
            {
                maxPeers = GRushMockNetParams.DefaultMaxPeers;
                topology = GRushTopology.Mesh;
                configured = false;
            }
        }

        // 設定の決まっていない部屋にいる AddPeer の相手が、定員に収まるか。
        private bool FitsCapacity(int capacity)
        {
            if (members.Count >= capacity)
            {
                return false;
            }
            return !members.Exists(member => member.Index >= capacity);
        }

        /// <summary>今の部屋の空いた席の数（定員から、自分を含む今の人数を引いた数）。</summary>
        public int FreeSeats
        {
            get { return Math.Max(0, maxPeers - members.Count); }
        }

        private int NextFreeIndex()
        {
            for (var index = 0; index < maxPeers; index++)
            {
                if (!members.Exists(member => member.Index == index))
                {
                    return index;
                }
            }
            return -1;
        }

        private int HostIndex()
        {
            var host = int.MaxValue;
            foreach (var member in members)
            {
                if (member.Index < host)
                {
                    host = member.Index;
                }
            }
            return host == int.MaxValue ? 0 : host;
        }

        private static GRushPeerWire WireOf(GRushMockMember member)
        {
            return new GRushPeerWire
            {
                index = member.Index,
                pseudoId = member.PseudoId,
                displayName = member.DisplayName,
                avatarUrl = member.AvatarUrl,
            };
        }

        private static string BuildPeerJson(GRushMockMember member)
        {
            var builder = new StringBuilder("{");
            builder.Append("\"index\":").Append(member.Index);
            builder.Append(",\"pseudoId\":").Append(GRushWire.Escape(member.PseudoId ?? string.Empty));
            builder.Append(",\"displayName\":");
            builder.Append(
                member.DisplayName == null ? "null" : GRushWire.Escape(member.DisplayName)
            );
            builder.Append(",\"avatarUrl\":");
            builder.Append(member.AvatarUrl == null ? "null" : GRushWire.Escape(member.AvatarUrl));
            return builder.Append('}').ToString();
        }
    }
}
