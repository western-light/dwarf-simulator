using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet.Voice
{
    [System.Serializable]
    public class SyncFilters : NetworkModule, IEnumerable<SyncFilters.Filter>
    {
        [SerializeField] private bool _ownerAuth = false;

        [SerializeField] private List<Filter> _filters = new();

        [NonSerialized][DontPack] private object _filtersLock;
        [NonSerialized][DontPack] private Filter[] _processingSnapshot = Array.Empty<Filter>();
        [NonSerialized][DontPack] private bool _processingSnapshotDirty = true;

        private object filtersLock
        {
            get
            {
                if (_filtersLock != null)
                    return _filtersLock;

                var newLock = new object();
                return Interlocked.CompareExchange(ref _filtersLock, newLock, null) ?? newLock;
            }
        }

        public int Count
        {
            get
            {
                lock (filtersLock)
                    return _filters.Count;
            }
        }

        public List<Filter> ToList()
        {
            lock (filtersLock)
                return new List<Filter>(_filters);
        }

        public List<Filter> list => _filters;

        public Filter this[int index]
        {
            get
            {
                lock (filtersLock)
                    return _filters[index];
            }
            set
            {
                lock (filtersLock)
                {
                    _filters[index] = value;
                    _processingSnapshotDirty = true;
                }
            }
        }

        public SyncFilters(bool ownerAuth = false)
        {
            _ownerAuth = ownerAuth;
        }

        public SyncFilters(SyncFilters filters)
        {
            _ownerAuth = filters._ownerAuth;
            _filters = new List<Filter>();

            var sourceFilters = filters.ToList();
            for (var i = 0; i < sourceFilters.Count; i++)
            {
                var filter = sourceFilters[i];
                var newFilter = new Filter(filter.audioFilter, filter.filterLevel, filter.strength);
                newFilter.Init();
                _filters.Add(newFilter);
            }
        }

        public override void OnObserverAdded(PlayerID player)
        {
            base.OnObserverAdded(player);

            if (player == localPlayer)
                return;

            List<Filter> receiverFilters;
            lock (filtersLock)
                receiverFilters = _filters.FindAll(f => f.filterLevel != FilterLevel.Sender);

            if (receiverFilters.Count > 0)
                BufferPlayer(player, receiverFilters);
        }

        [TargetRpc]
        private void BufferPlayer(PlayerID player, List<Filter> filters)
        {
            lock (filtersLock)
            {
                for (var i = _filters.Count - 1; i >= 0; i--)
                {
                    if (_filters[i].filterLevel != FilterLevel.Sender)
                        _filters.RemoveAt(i);
                }

                for (var i = 0; i < filters.Count; i++)
                {
                    var filter = filters[i];
                    var newFilter = new Filter(filter.audioFilter, filter.filterLevel, filter.strength);
                    newFilter.Init();
                    _filters.Add(newFilter);
                }

                _processingSnapshotDirty = true;
            }
        }

        public void AddFilter(PurrAudioFilter filter, FilterLevel level, float initialStrength = 1)
        {
            if (level == FilterLevel.Sender)
            {
                AddFilter_Internal(filter, level, initialStrength);
                return;
            }

            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogError($"Only the controller can add filters | Owner auth: {_ownerAuth} | Is Owner: {isOwner} | IsServer: {isServer}", parent);
                return;
            }

            AddFilter_Internal(filter, level, initialStrength);
            AddFilterServerRpc(filter, level, initialStrength);
        }

        [ServerRpc]
        private void AddFilterServerRpc(PurrAudioFilter filter, FilterLevel level, float initialStrength)
        {
            AddFilterObservers(filter, level, initialStrength);
            if (!IsController(_ownerAuth))
                AddFilter_Internal(filter, level, initialStrength);
        }

        [ObserversRpc(excludeSender:true)]
        private void AddFilterObservers(PurrAudioFilter filter, FilterLevel level, float initialStrength)
        {
            if (!IsController(_ownerAuth))
                AddFilter_Internal(filter,  level, initialStrength);
        }

        public void AddFilterLocal(PurrAudioFilter filter, FilterLevel level, float initialStrength = 1)
        {
            AddFilter_Internal(filter, level, initialStrength);
        }

        public void RemoveFilterLocal(int filterIndex)
        {
            RemoveFilter_Internal(filterIndex);
        }

        private void AddFilter_Internal(PurrAudioFilter filter, FilterLevel level, float initialStrength)
        {
            var newFilter = new Filter(filter, level, initialStrength);
            newFilter.Init();
            lock (filtersLock)
            {
                _filters.Add(newFilter);
                _processingSnapshotDirty = true;
            }
        }

        public void RemoveFilter(int filterIndex)
        {
            var filters = ToList();
            if (filterIndex < 0 || filterIndex >= filters.Count)
            {
                PurrLogger.LogError($"Invalid filter index: {filterIndex}. Cannot remove filter.");
                return;
            }

            bool isSender = filters[filterIndex].filterLevel == FilterLevel.Sender;

            if (isSender)
            {
                RemoveFilter_Internal(filterIndex);
                return;
            }

            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogError($"Only the controller can remove filters | Owner auth: {_ownerAuth}", parent);
                return;
            }

            int remoteIndex = 0;
            for (int i = 0; i < filterIndex; i++)
            {
                if (filters[i].filterLevel != FilterLevel.Sender)
                    remoteIndex++;
            }
            RemoveFilter_Internal(filterIndex);
            RemoveFilterServerRpc(remoteIndex);
        }
        
        [ServerRpc]
        private void RemoveFilterServerRpc(int filterIndex)
        {
            RemoveFilterObservers(filterIndex);
            if (!IsController(_ownerAuth))
                RemoveFilter_Internal(filterIndex);
        }

        [ObserversRpc(excludeSender:true)]
        private void RemoveFilterObservers(int filterIndex)
        {
            if (!IsController(_ownerAuth))
                RemoveFilter_Internal(filterIndex);
        }
        
        private void RemoveFilter_Internal(int filterIndex)
        {
            lock (filtersLock)
            {
                if (filterIndex < 0 || filterIndex >= _filters.Count)
                {
                    PurrLogger.LogError($"Invalid filter index: {filterIndex}. Cannot remove filter.");
                    return;
                }

                _filters.RemoveAt(filterIndex);
                _processingSnapshotDirty = true;
            }
        }

        internal Filter[] GetProcessingSnapshot()
        {
            lock (filtersLock)
            {
                if (_processingSnapshotDirty || _processingSnapshot == null)
                {
                    _processingSnapshot = _filters.Count == 0 ? Array.Empty<Filter>() : _filters.ToArray();
                    _processingSnapshotDirty = false;
                }

                return _processingSnapshot;
            }
        }

        public IEnumerator<Filter> GetEnumerator() => ToList().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        
        [System.Serializable]
        public struct Filter
        {
            [SerializeField] private PurrAudioFilter _filter;
            [SerializeField] private FilterLevel _filterLevel;
            public float strength;

            [NonSerialized][DontPack] private FilterInstance _instance;
            [NonSerialized][DontPack] private string _filterName;

            public PurrAudioFilter audioFilter => _filter;
            public FilterLevel filterLevel => _filterLevel;

            public Filter(PurrAudioFilter filter, FilterLevel level, float initialStrength)
            {
                _filter = filter;
                _filterLevel = level;
                strength = initialStrength;
                _instance = null;
                _filterName = null;
            }

            public void Init()
            {
                if (_filter != null)
                {
                    _filterName = _filter.name;
                    _instance = _filter.CreateInstance();
                }
            }

            public void Process(ArraySegment<float> inputSamples, int frequency, FilterLevel level)
            {
                if (level != _filterLevel)
                    return;

                if (_instance == null)
                {
                    PurrLogger.LogError($"Attempting to process filter without instance: {_filterName ?? "null"}");
                    return;
                }

                _instance.Process(inputSamples, frequency, strength);

                if (_instance.SupportsPerSampleProcessing)
                {
                    float[] arr = inputSamples.Array;
                    int off = inputSamples.Offset;
                    int count = inputSamples.Count;
                    for (int i = 0; i < count; i++)
                    {
                        float sample = arr[off + i];
                        _instance.Process(ref sample, frequency, strength);
                        arr[off + i] = sample;
                    }
                }
            }
        }

        public void Clear()
        {
            lock (filtersLock)
            {
                _filters.Clear();
                _processingSnapshot = Array.Empty<Filter>();
                _processingSnapshotDirty = false;
            }
        }

        public void AddRange(SyncFilters audioFilters)
        {
            var filters = audioFilters.ToList();
            var newFilters = new List<Filter>(filters.Count);

            for (var i = 0; i < filters.Count; i++)
            {
                var filter = filters[i];
                var newFilter = new Filter(filter.audioFilter, filter.filterLevel, filter.strength);
                newFilter.Init();
                newFilters.Add(newFilter);
            }

            lock (filtersLock)
            {
                _filters.AddRange(newFilters);
                _processingSnapshotDirty = true;
            }
        }
    }
    
    public enum FilterLevel
    {
        Sender,
        Receiver,
    }
}
