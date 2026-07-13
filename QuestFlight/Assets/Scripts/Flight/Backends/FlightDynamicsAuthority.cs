using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// Process-wide guard preventing two backends from integrating one aircraft
    /// simulation root at the same time.
    /// </summary>
    public static class FlightDynamicsAuthority
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<int, object> Owners = new Dictionary<int, object>();

        public static int ActiveAuthorityCount
        {
            get
            {
                lock (Sync) return Owners.Count;
            }
        }

        public static bool TryAcquire(
            Transform simulationRoot,
            object owner,
            out FlightDynamicsAuthorityLease lease,
            out string error)
        {
            lease = null;
            error = string.Empty;
            if (simulationRoot == null)
            {
                error = "An aircraft simulation root is required.";
                return false;
            }

            if (owner == null)
            {
                error = "An authority owner is required.";
                return false;
            }

            int rootId = simulationRoot.GetInstanceID();
            lock (Sync)
            {
                if (Owners.TryGetValue(rootId, out object existing))
                {
                    error = ReferenceEquals(existing, owner)
                        ? $"{simulationRoot.name} already has an authority lease for this owner."
                        : $"{simulationRoot.name} already has another authoritative flight backend.";
                    return false;
                }

                Owners.Add(rootId, owner);
                lease = new FlightDynamicsAuthorityLease(rootId, owner);
                return true;
            }
        }

        internal static void Release(int rootId, object owner)
        {
            lock (Sync)
            {
                if (Owners.TryGetValue(rootId, out object existing) && ReferenceEquals(existing, owner))
                {
                    Owners.Remove(rootId);
                }
            }
        }
    }

    public sealed class FlightDynamicsAuthorityLease : IDisposable
    {
        private readonly int _rootId;
        private object _owner;

        internal FlightDynamicsAuthorityLease(int rootId, object owner)
        {
            _rootId = rootId;
            _owner = owner;
        }

        public void Dispose()
        {
            object owner = _owner;
            if (owner == null) return;
            _owner = null;
            FlightDynamicsAuthority.Release(_rootId, owner);
        }
    }
}
