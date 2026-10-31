using System;
using GRushSdk;
using UnityEngine;

public class GRushDuelSample : MonoBehaviour
{
    private const byte PaddleKind = 1;
    private const byte BallKind = 2;
    private const float SendIntervalSec = 0.05f;

    private GRushRoom room;
    private GRushMockPeer sparring;
    private string status = "Joining a duel room...";
    private float localPaddleY = 0.5f;
    private float remotePaddleY = 0.5f;
    private Vector2 ball = new Vector2(0.5f, 0.5f);
    private Vector2 ballVelocity = new Vector2(0.35f, 0.22f);
    private float sendTimer;
    private readonly byte[] paddleFrame = new byte[5];
    private readonly byte[] sparringFrame = new byte[5];
    private readonly byte[] ballFrame = new byte[9];
    private float paddleInput;

    private async void Start()
    {
        if (!GRush.IsAvailable)
        {
            status = "Running outside GameRush.";
            return;
        }
        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            sparring = GRushMock.AddPeer("Sparring Partner");
            if (sparring != null)
            {
                sparring.Received += OnSparringMessage;
            }
        }

        var joined = await GRush.Net.JoinAsync("duel");
        if (!joined.Ok)
        {
            status = "Could not join: " + joined.Message;
            return;
        }
        room = joined.Value;
        room.Message += OnMessage;
        room.PeerJoined += peer => status = "Opponent joined: " + (peer.DisplayName ?? "guest");
        room.PeerLeft += index => status = "Opponent left (peer " + index + ").";
        room.TransportChanged += transport => status = "Transport is now " + transport + ".";
        room.Error += error => Debug.LogWarning("Room dropped a message: " + error.Code);
        room.Closed += reason => status = "Room closed: " + reason;
        status = room.IsHost ? "You are the host." : "Waiting for the host.";
    }

    private void OnDestroy()
    {
        if (sparring != null)
        {
            GRushMock.RemovePeer(sparring);
        }
        if (room != null && !room.IsClosed)
        {
            _ = room.LeaveAsync();
        }
    }

    private void Update()
    {
        if (room == null || room.IsClosed)
        {
            return;
        }
        localPaddleY = Mathf.Clamp01(localPaddleY + paddleInput * Time.deltaTime);
        if (room.IsHost)
        {
            SimulateBall();
        }

        sendTimer += Time.deltaTime;
        if (sendTimer < SendIntervalSec)
        {
            return;
        }
        sendTimer = 0f;
        WriteFloat(paddleFrame, PaddleKind, 1, localPaddleY);
        room.Send(paddleFrame, GRushChannel.Unreliable, GRushRoom.Everyone);
        if (room.IsHost)
        {
            WriteFloat(ballFrame, BallKind, 1, ball.x);
            BitConverter.GetBytes(ball.y).CopyTo(ballFrame, 5);
            room.Send(ballFrame, GRushChannel.Unreliable, GRushRoom.Everyone);
        }
    }

    private void SimulateBall()
    {
        ball += ballVelocity * Time.deltaTime;
        if (ball.y <= 0f || ball.y >= 1f)
        {
            ballVelocity.y = -ballVelocity.y;
            ball.y = Mathf.Clamp01(ball.y);
        }
        if (ball.x <= 0f || ball.x >= 1f)
        {
            ballVelocity.x = -ballVelocity.x;
            ball.x = Mathf.Clamp01(ball.x);
        }
    }

    private void OnMessage(GRushMessage message)
    {
        if (message.IsStale || message.Payload == null || message.Payload.Length < 5)
        {
            return;
        }
        if (message.Payload[0] == PaddleKind)
        {
            remotePaddleY = BitConverter.ToSingle(message.Payload, 1);
            return;
        }
        if (message.Payload[0] == BallKind && !room.IsHost && message.Payload.Length >= 9)
        {
            ball = new Vector2(
                BitConverter.ToSingle(message.Payload, 1),
                BitConverter.ToSingle(message.Payload, 5)
            );
        }
    }

    private void OnSparringMessage(GRushMessage message)
    {
        if (message.Payload == null || message.Payload.Length < 9 || message.Payload[0] != BallKind)
        {
            return;
        }
        WriteFloat(sparringFrame, PaddleKind, 1, BitConverter.ToSingle(message.Payload, 5));
        sparring.Send(sparringFrame, GRushChannel.Unreliable, GRushRoom.Everyone);
    }

    private static void WriteFloat(byte[] frame, byte kind, int offset, float value)
    {
        frame[0] = kind;
        BitConverter.GetBytes(value).CopyTo(frame, offset);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(16, 16, 420, 200));
        GUILayout.Label("GameRush Duel");
        GUILayout.Label(status);
        if (room != null)
        {
            GUILayout.Label(
                "Peer " + room.LocalPeerId + " / host " + room.HostPeerId + " / " + room.Stats.Transport
            );
            GUILayout.Label("Server time: " + room.ServerTimeMs());
            GUILayout.Label("Room code: " + room.RoomCode);
        }
        GUILayout.BeginHorizontal();
        var up = GUILayout.RepeatButton("Up", GUILayout.Width(80f));
        var down = GUILayout.RepeatButton("Down", GUILayout.Width(80f));
        GUILayout.EndHorizontal();
        if (Event.current.type == EventType.Repaint)
        {
            paddleInput = up ? -1f : down ? 1f : 0f;
        }
        GUILayout.EndArea();

        var field = new Rect(Screen.width * 0.5f - 150f, 200f, 300f, 300f);
        GUI.Box(field, GUIContent.none);
        GUI.DrawTexture(
            new Rect(field.x + 6f, field.y + field.height * localPaddleY - 20f, 10f, 40f),
            Texture2D.whiteTexture
        );
        GUI.DrawTexture(
            new Rect(field.xMax - 16f, field.y + field.height * remotePaddleY - 20f, 10f, 40f),
            Texture2D.whiteTexture
        );
        GUI.DrawTexture(
            new Rect(field.x + field.width * ball.x - 5f, field.y + field.height * ball.y - 5f, 10f, 10f),
            Texture2D.whiteTexture
        );
    }
}
