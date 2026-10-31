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

        public GRushJoinWire Join(
            string mode,
            string requestedCode,
            string pseudoId,
            string displayName,
            string avatarUrl,
            Action<GRushNetEvent> deliver
        )
        {
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
        }

        public bool RaiseError(string code, string message)
        {
            var detail =
                "{\"code\":"
                + GRushWire.Escape(code ?? string.Empty)
                + ",\"message\":"
                + GRushWire.Escape(message ?? string.Empty)
                + "}";
            var delivered = false;
            foreach (var member in members.ToArray())
            {
                if (member.Peer != null || member.Deliver == null)
                {
                    continue;
                }
                var deliver = member.Deliver;
                GRushDispatcher.Post(
                    () => deliver(new GRushNetEvent { Kind = GRushNetEventKind.Error, Detail = detail })
                );
                delivered = true;
            }
            return delivered;
        }

        public void Route(int from, byte[] payload, int count, GRushChannel channel, int to)
        {
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

        private int NextFreeIndex()
        {
            for (var index = 0; index < GRushMockLimits.MaxPeers; index++)
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

    internal static class GRushMockLimits
    {
        public const int MaxPeers = 8;
    }
}
