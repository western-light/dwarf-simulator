using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using PurrNet.Transports;
using UnityEngine;

#if PURR_PIPES
using PurrNet.Pipes;
#endif

namespace PurrNet.Voice
{
    [Serializable]
    public partial class NetworkAudioModule : NetworkModule, IAudioInputSource
    {
#if PURR_PIPES
        [SerializeField] private NetworkPipe _pipe;
#endif
        private float[] _chunkBuffer;
        private float[] _decodeBuffer;
        private byte[] _encodeBuffer;
        private byte[] _rawSendBuffer;
        private float[] _rawReceiveBuffer;
        private int _rawReceivePos;
        private int _chunkSize;
        private int _bufferPos;
        private readonly SyncVar<int> _frequency = new(-1, ownerAuth:true);
        private IAudioCodec _codec;

        private int _micFrequency;
        private double _resampleCarry;
        private readonly HashSet<PlayerID> _observerCache = new();
        private readonly SilenceSuppressor _silenceSuppressor = new SilenceSuppressor();

        private const byte FLAG_RESUME = 0x01;
        private const byte FLAG_SEQUENCED = 0x80;
        private const int SEQUENCED_HEADER_SIZE = 5;
        private byte[] _wireBuffer;
        private uint _nextSequence;
        private uint _lastReceivedSequence;
        private bool _hasReceivedSequence;

        private void EnsureWireBuffer(int neededLen)
        {
            if (_wireBuffer == null || _wireBuffer.Length < neededLen)
                _wireBuffer = new byte[Math.Max(neededLen, 256)];
        }

        internal void SetSilenceSuppression(SilenceSuppressionSettings s)
        {
            _silenceSuppressor.settings = s;
            _silenceSuppressor.Reset();
        }

        public IAudioCodec codec => _codec;
        public int chunkSize => _chunkSize;
        public int micFrequency => _micFrequency;

        public event Action<int> OnFrequencyChanged
        {
            add => _frequency.onChanged += value;
            remove => _frequency.onChanged -= value;
        }

        public int frequency => _frequency;

        public bool isRecording { get; private set; }

        public event Action<ArraySegment<float>> onSampleReady;

        private const int MAX_CHUNK_SIZE_BYTES = 900;


        public NetworkAudioModule()
        {
            _frequency.onChanged += OnFrequencySet;
        }

        public void SetCodec(IAudioCodec codec)
        {
            _codec?.Dispose();
            _codec = codec;
            _hasReceivedSequence = false;

            _silenceSuppressor.Reset();

            if (_frequency.value > 0)
                OnFrequencySet(_frequency.value);
        }

#if PURR_PIPES
        public override void OnEarlySpawn()
        {
            if (_pipe)
                _pipe.onDataReceivedUntyped += OnPipeData;
        }

        public override void OnDespawned()
        {
            if (_pipe)
                _pipe.onDataReceivedUntyped -= OnPipeData;
        }
#endif

        private void OnFrequencySet(int newFreq)
        {
            if (newFreq <= 0)
            {
                Debug.LogWarning($"[PurrVoice] Ignoring invalid network frequency ({newFreq}) for '{(parent ? parent.name : "<no parent>")}'.", parent);
                return;
            }

            if (_codec != null && _codec.TargetSampleRate != newFreq)
            {
                Debug.LogWarning($"[PurrVoice] Codec target rate ({_codec.TargetSampleRate}) does not match network frequency ({newFreq}). " +
                                 $"Recreating codec at correct rate.");

                int frameSize = newFreq / 50;
                OpusCodec recreated;
                try
                {
                    recreated = new OpusCodec(newFreq, 1, frameSize);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PurrVoice] Failed to recreate codec at {newFreq} Hz; keeping the existing codec. {e.Message}", parent);
                    return;
                }

                _codec.Dispose();
                _codec = recreated;
                Debug.Log($"[PurrVoice] Recreated decoder codec at {newFreq} Hz, frame size {frameSize}.");
            }

            _chunkSize = newFreq / 50;
            _chunkBuffer = new float[_chunkSize];
            _decodeBuffer = new float[_chunkSize];

            if (_codec != null)
            {
                _encodeBuffer = new byte[OpusCodec.MaxEncodedBytes];
            }
            else
            {
                _rawSendBuffer = new byte[_chunkSize * sizeof(float)];
                _rawReceiveBuffer = new float[_chunkSize];
                _rawReceivePos = 0;
            }

            _bufferPos = 0;
        }

        public void SetFrequency(int micFrequency)
        {
            if (!isController)
            {
                Debug.LogError($"Only the controller can set the frequency. Current controller: {owner}, current player: {localPlayer}");
                return;
            }

            _micFrequency = micFrequency;
            _resampleCarry = 0d;

            if (_codec != null)
                _frequency.value = _codec.TargetSampleRate;
            else
                _frequency.value = micFrequency;
        }

        public void SendAudioChunk(ArraySegment<float> segment)
        {
            if (!isOwner || _frequency.value < 0 || _chunkBuffer.Length <= 0) return;
            if (_codec != null && _codec.TargetSampleRate <= 0) return;

            int targetRate = _frequency.value;

            if (_micFrequency > 0 && _micFrequency != targetRate)
            {
                int outCount = (int)((segment.Count * (double)targetRate / _micFrequency) + _resampleCarry);
                _resampleCarry += (segment.Count * (double)targetRate / _micFrequency) - outCount;

                var resampled = ArrayPool<float>.Shared.Rent(outCount);
                try
                {
                    float ratio = _micFrequency / (float)targetRate;
                    for (int i = 0; i < outCount; i++)
                    {
                        float srcIdx = i * ratio;
                        int s0 = (int)srcIdx;
                        int s1 = Math.Min(s0 + 1, segment.Count - 1);
                        float frac = srcIdx - s0;
                        resampled[i] = segment.Array![segment.Offset + s0] * (1f - frac)
                                     + segment.Array![segment.Offset + s1] * frac;
                    }

                    SendAudioChunkInternal(new ArraySegment<float>(resampled, 0, outCount));
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(resampled);
                }
            }
            else
            {
                SendAudioChunkInternal(segment);
            }
        }

        private void SendAudioChunkInternal(ArraySegment<float> segment)
        {
            int offset = 0;
            while (offset < segment.Count)
            {
                int remaining = _chunkSize - _bufferPos;
                int copy = Math.Min(remaining, segment.Count - offset);
                if (copy <= 0) break;
                Array.Copy(segment.Array!, segment.Offset + offset, _chunkBuffer, _bufferPos, copy);
                _bufferPos += copy;
                offset += copy;

                if (_bufferPos == _chunkSize)
                {
                    if (!_silenceSuppressor.ShouldSend(_chunkBuffer, 0, _chunkSize, out bool resuming))
                    {
                        _bufferPos = 0;
                        continue;
                    }

                    (parent as PurrVoicePlayer)?.DebugNetworkSentData(_chunkBuffer);

                    if (_codec != null)
                    {
                        int encodedLen;
                        try
                        {
                            if (resuming)
                                _codec.ResetEncoderState();

                            encodedLen = _codec.Encode(_chunkBuffer, 0, _chunkSize, _encodeBuffer);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e, parent);
                            _silenceSuppressor.Reset();
                            _bufferPos = 0;
                            continue;
                        }

                        SendPayload(_encodeBuffer, 0, encodedLen, resuming, true,
                            _chunkBuffer, 0, _chunkSize);
                    }
                    else
                    {
                        int rawLen = _chunkSize * sizeof(float);
                        Buffer.BlockCopy(_chunkBuffer, 0, _rawSendBuffer, 0, rawLen);
                        SendPayload(_rawSendBuffer, 0, rawLen, resuming, false,
                            _chunkBuffer, 0, _chunkSize);
                    }

                    _bufferPos = 0;
                }
            }
        }

        public bool SendPreEncoded(byte[] data, int offset, int length, bool resume)
        {
            if (!isOwner || length <= 0)
                return false;

            SendPayload(data, offset, length, resume, _codec != null, null, 0, 0);
            return true;
        }

        private void SendPayload(byte[] data, int offset, int length, bool resume, bool encoded,
            float[] samples, int sampleOffset, int sampleCount)
        {
            uint sequence = _nextSequence++;
            byte flags = (byte)(FLAG_SEQUENCED | (resume ? FLAG_RESUME : 0));
            int wireLen = length + SEQUENCED_HEADER_SIZE;
            EnsureWireBuffer(wireLen);
            _wireBuffer[0] = flags;
            VoiceDiagnosticWire.WriteUInt32(_wireBuffer, 1, sequence);

            if (!ReferenceEquals(data, _wireBuffer) || offset != SEQUENCED_HEADER_SIZE)
                Buffer.BlockCopy(data, offset, _wireBuffer, SEQUENCED_HEADER_SIZE, length);

            _diagnosticCapture.RecordSent(data, offset, length, sequence, flags,
                encoded, samples, sampleOffset, sampleCount);
            BroadcastWire(_wireBuffer, wireLen);
        }

        private void BroadcastWire(byte[] wire, int wireLen)
        {
            int wireOffset = 0;
            while (wireOffset < wireLen)
            {
                int sliceLen = Math.Min(MAX_CHUNK_SIZE_BYTES, wireLen - wireOffset);
                var data = new ByteData(wire, wireOffset, sliceLen);
#if PURR_PIPES
                if (_pipe)
                    _pipe.Broadcast(data, Channel.Unreliable);
                else RpcSendAudio(data);
#else
                RpcSendAudio(data);
#endif
                wireOffset += sliceLen;
            }
        }

#if PURR_PIPES
        private void OnPipeData(PlayerID player, ByteData encoded)
        {
            ReceiveAudioEncoded(encoded);
        }
#endif

        private bool _loggedRejectedAudioSender;

        [ServerRpc(channel: Channel.Unreliable, requireOwnership: false)]
        private void RpcSendAudio(ByteData encoded, RPCInfo info = default)
        {
            if (encoded.data == null || encoded.length <= 0)
                return;

            if (owner.HasValue && info.sender != owner.Value)
            {
                if (!_loggedRejectedAudioSender)
                {
                    _loggedRejectedAudioSender = true;
                    Debug.LogWarning($"[PurrVoice] Rejected audio from {info.sender}; current owner is {owner.Value}.", parent);
                }
                return;
            }

            var observers = parent.observers;
            _observerCache.Clear();
            for (int i = 0; i < observers.Count; i++)
                _observerCache.Add(observers[i]);

            for (var i = 0; i < networkManager.players.Count; i++)
            {
                var player = networkManager.players[i];
                if (!_observerCache.Contains(player)) continue;
                if (owner == player) continue;

                if (player == localPlayer)
                {
                    if (_codec != null || _decodeBuffer != null)
                        ReceiveAudioEncoded(encoded);
                    continue;
                }

                TargetReceiveAudio(player, encoded);
            }
        }

        [TargetRpc(channel: Channel.Unreliable)]
        private void TargetReceiveAudio(PlayerID player, ByteData encoded)
        {
            ReceiveAudioEncoded(encoded);
        }

        private bool _loggedNullCodecWarning;
        private int _receivedPackets;
        private int _decodedPackets;
        private int _droppedBeforeReady;
        private bool _loggedDropBeforeReady;
        private bool _loggedFirstDecode;

        /// <summary>
        /// Voice packets received from the network for this stream, before decoding.
        /// </summary>
        public int receivedPacketCount => _receivedPackets;

        /// <summary>
        /// Voice packets decoded into samples for this stream.
        /// </summary>
        public int decodedPacketCount => _decodedPackets;

        /// <summary>
        /// Voice packets dropped because they arrived before the stream frequency was known.
        /// </summary>
        public int droppedBeforeReadyCount => _droppedBeforeReady;

        private void ReceiveAudioEncoded(ByteData encoded)
        {
            if (encoded.data == null || encoded.length < 1)
                return;

            _receivedPackets++;

            if (_decodeBuffer == null)
            {
                _droppedBeforeReady++;
                if (!_loggedDropBeforeReady)
                {
                    _loggedDropBeforeReady = true;
                    Debug.Log($"[PurrVoice] Voice data for '{(parent ? parent.name : "<no parent>")}' arrived before its stream frequency was known (frequency SyncVar = {_frequency.value}); dropping until it arrives.", parent);
                }
                return;
            }

            byte flags = encoded.data[encoded.offset];
            bool resetRequested = (flags & FLAG_RESUME) != 0;
            bool sequenced = (flags & FLAG_SEQUENCED) != 0;
            int headerSize = sequenced ? SEQUENCED_HEADER_SIZE : 1;
            if (encoded.length < headerSize)
                return;

            uint sequence = sequenced ? VoiceDiagnosticWire.ReadUInt32(encoded.data, encoded.offset + 1) : 0;
            VoiceDiagnosticDelivery delivery = ResolveDelivery(sequenced, sequence, out int missingBefore);
            int payloadOffset = encoded.offset + headerSize;
            int payloadLen = encoded.length - headerSize;

            if (payloadLen <= 0)
                return;

            if (_codec != null)
            {
                if (resetRequested)
                    _codec.ResetDecoderState();

                int sampleCount = _codec.Decode(encoded.data, payloadOffset, payloadLen, _decodeBuffer);
                NoteDecoded();
                _diagnosticCapture.RecordReceived(encoded.data, payloadOffset, payloadLen, sequence, flags,
                    delivery, missingBefore, true, _decodeBuffer, 0, sampleCount);
                var segment = new ArraySegment<float>(_decodeBuffer, 0, sampleCount);
                (parent as PurrVoicePlayer)?.DebugReceived(segment);
                onSampleReady?.Invoke(segment);
                return;
            }

            if (!_loggedNullCodecWarning && _frequency.value > 0)
            {
                _loggedNullCodecWarning = true;
                Debug.Log($"[PurrVoice] Receiving raw PCM audio (no codec). Frequency: {_frequency.value}");
            }

            int floatCount = payloadLen / sizeof(float);
            NoteDecoded();
            _diagnosticCapture.RecordReceived(encoded.data, payloadOffset, payloadLen, sequence, flags,
                delivery, missingBefore, false, null, 0, 0);
            if (_rawReceiveBuffer == null || _chunkSize <= 0)
            {
                if (floatCount > _decodeBuffer.Length)
                    floatCount = _decodeBuffer.Length;
                Buffer.BlockCopy(encoded.data, payloadOffset, _decodeBuffer, 0, floatCount * sizeof(float));
                var segment = new ArraySegment<float>(_decodeBuffer, 0, floatCount);
                (parent as PurrVoicePlayer)?.DebugReceived(segment);
                onSampleReady?.Invoke(segment);
                return;
            }

            int srcOffset = payloadOffset;
            int remaining = floatCount;

            while (remaining > 0)
            {
                int space = _chunkSize - _rawReceivePos;
                int copy = Math.Min(space, remaining);
                Buffer.BlockCopy(encoded.data, srcOffset, _rawReceiveBuffer, _rawReceivePos * sizeof(float), copy * sizeof(float));
                _rawReceivePos += copy;
                srcOffset += copy * sizeof(float);
                remaining -= copy;

                if (_rawReceivePos >= _chunkSize)
                {
                    var segment = new ArraySegment<float>(_rawReceiveBuffer, 0, _chunkSize);
                    (parent as PurrVoicePlayer)?.DebugReceived(segment);
                    onSampleReady?.Invoke(segment);
                    _rawReceivePos = 0;
                }
            }
        }

        private void NoteDecoded()
        {
            _decodedPackets++;
            if (_loggedFirstDecode)
                return;

            _loggedFirstDecode = true;
            string dropped = _droppedBeforeReady > 0 ? $" after dropping {_droppedBeforeReady} early packet(s)" : string.Empty;
            Debug.Log($"[PurrVoice] Decoding voice for '{(parent ? parent.name : "<no parent>")}' at {_frequency.value} Hz{dropped}.", parent);
        }

        private VoiceDiagnosticDelivery ResolveDelivery(bool sequenced, uint sequence, out int missingBefore)
        {
            missingBefore = 0;
            if (!sequenced)
                return VoiceDiagnosticDelivery.Unsequenced;

            if (!_hasReceivedSequence)
            {
                _hasReceivedSequence = true;
                _lastReceivedSequence = sequence;
                return VoiceDiagnosticDelivery.First;
            }

            uint delta = sequence - _lastReceivedSequence;
            if (delta == 0)
                return VoiceDiagnosticDelivery.Duplicate;

            if (delta < 0x80000000u)
            {
                _lastReceivedSequence = sequence;
                missingBefore = (int)(delta - 1);
                return missingBefore > 0 ? VoiceDiagnosticDelivery.Gap : VoiceDiagnosticDelivery.InOrder;
            }

            return VoiceDiagnosticDelivery.OutOfOrder;
        }

        public StartDeviceResult Start()
        {
            isRecording = true;
            return StartDeviceResult.Success;
        }

        public void Stop()
        {
            isRecording = false;
            _silenceSuppressor.Reset();
        }

        public void DisposeCodec()
        {
            _codec?.Dispose();
            _codec = null;
        }
    }
}
