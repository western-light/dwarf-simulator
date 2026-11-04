using System;
using System.Collections.Generic;
using System.Text;
using PurrNet.Lobby;
using UnityEngine;

namespace DwarfSimulator.Lobby.EOS
{
    /// <summary>
    /// Chat over member attributes, since EOS lobbies have no messaging and the EOS transport
    /// drops P2P packets on other sockets. Each player publishes their last few messages as
    /// "seq:base64;seq:base64" in <see cref="EOSLobbyKeys.Chat"/>; receivers dispatch the
    /// sequence numbers they have not seen yet. Keeping several entries means a burst of
    /// messages batched into one update is not lost.
    /// </summary>
    public class EOSChat : LobbyChatBase
    {
        private const int MaxEntries = 4;
        private const int MaxAttributeLength = 1000;

        private readonly EOSLobby _lobby;
        private readonly List<(long seq, string payload)> _outbox = new();
        private readonly Dictionary<string, long> _lastSeen = new();
        private long _nextSeq;

        protected override IPlayer localPlayer => _lobby.localPlayer;

        internal EOSChat(EOSLobby lobby)
        {
            _lobby = lobby;
        }

        protected override void SendToProvider(byte[] data)
        {
            _outbox.Add((++_nextSeq, Convert.ToBase64String(data)));
            if (_outbox.Count > MaxEntries)
                _outbox.RemoveAt(0);

            var encoded = Encode(_outbox);
            while (encoded.Length > MaxAttributeLength && _outbox.Count > 1)
            {
                _outbox.RemoveAt(0);
                encoded = Encode(_outbox);
            }

            if (encoded.Length > MaxAttributeLength)
            {
                Debug.LogWarning("[EOSChat] Message too long to send over an EOS lobby attribute.");
                _outbox.Clear();
                return;
            }

            _lobby.QueueMemberWrite(EOSLobbyKeys.Chat, encoded);
        }

        /// <summary>
        /// Called by <see cref="EOSLobby"/> with a remote player's chat attribute. The first
        /// sighting of a player only records their latest sequence, so history is not replayed.
        /// </summary>
        internal void OnChatAttribute(IPlayer sender, string encoded)
        {
            var entries = Decode(encoded);
            bool known = _lastSeen.TryGetValue(sender.id, out var last);
            long newest = last;

            foreach (var (seq, payload) in entries)
            {
                if (seq <= last)
                    continue;

                newest = Math.Max(newest, seq);
                if (!known)
                    continue;

                byte[] data;
                try { data = Convert.FromBase64String(payload); }
                catch (FormatException) { continue; }

                ReceiveFromProvider(sender, data);
            }

            _lastSeen[sender.id] = newest;
        }

        internal void Forget(string playerId) => _lastSeen.Remove(playerId);

        private static string Encode(List<(long seq, string payload)> entries)
        {
            var sb = new StringBuilder();
            foreach (var (seq, payload) in entries)
            {
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(seq).Append(':').Append(payload);
            }

            return sb.ToString();
        }

        private static List<(long seq, string payload)> Decode(string encoded)
        {
            var entries = new List<(long, string)>();
            if (string.IsNullOrEmpty(encoded))
                return entries;

            foreach (var entry in encoded.Split(';'))
            {
                int colon = entry.IndexOf(':');
                if (colon <= 0 || !long.TryParse(entry.Substring(0, colon), out var seq))
                    continue;
                entries.Add((seq, entry.Substring(colon + 1)));
            }

            entries.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return entries;
        }
    }
}
