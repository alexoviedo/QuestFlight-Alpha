using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    public enum CockpitInputSourceKind
    {
        Unknown = 0,
        OvrHand = 1,
        OvrTouchController = 2,
        Gamepad = 3
    }

    /// <summary>
    /// Serialized mapping from Meta Interaction SDK interactor identifiers to
    /// product side/modality. It never guesses from event metadata or names at
    /// runtime; the Editor authoring pass records every supported source.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OvrPointerSourceRegistry : MonoBehaviour
    {
        [Serializable]
        public struct Source
        {
            public MonoBehaviour interactor;
            public CockpitTouchInteractionV2Hand hand;
            public CockpitInputSourceKind kind;
        }

        [SerializeField] private Source[] sources = Array.Empty<Source>();

        private readonly Dictionary<int, Source> _byIdentifier = new();

        public int SourceCount => sources?.Length ?? 0;

        public void ConfigureAuthoredSources(Source[] authoredSources)
        {
            sources = authoredSources ?? Array.Empty<Source>();
            // Meta assigns an interactor's runtime identifier during its own
            // initialization. Authoring and prefab import occur before that
            // lifecycle, so only serialize the references here.
            _byIdentifier.Clear();
        }

        public bool ValidateAuthoredSources(out string error)
        {
            if (sources == null || sources.Length == 0)
            {
                error = "No serialized Meta Interaction SDK sources are registered.";
                return false;
            }

            var seen = new HashSet<MonoBehaviour>();
            for (int i = 0; i < sources.Length; i++)
            {
                Source source = sources[i];
                if (source.interactor == null ||
                    !(source.interactor is IInteractorView) ||
                    source.hand == CockpitTouchInteractionV2Hand.Either ||
                    source.kind == CockpitInputSourceKind.Unknown ||
                    !seen.Add(source.interactor))
                {
                    error = $"Serialized Meta source {i} is invalid or duplicated.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        public bool TryResolve(
            int identifier,
            out CockpitTouchInteractionV2Hand hand,
            out CockpitInputSourceKind kind)
        {
            if (_byIdentifier.Count == 0)
                BuildLookup();
            if (_byIdentifier.TryGetValue(identifier, out Source source))
            {
                hand = source.hand;
                kind = source.kind;
                return true;
            }


            // Meta owns interactor lifecycle and may assign/reassign runtime
            // identifiers after this component's Start during a concurrent
            // hand/controller transition. Rebuild only on a miss; normal
            // pointer dispatch stays allocation-free and O(1).
            BuildLookup();
            if (_byIdentifier.TryGetValue(identifier, out source))
            {
                hand = source.hand;
                kind = source.kind;
                return true;
            }

            hand = CockpitTouchInteractionV2Hand.Either;
            kind = CockpitInputSourceKind.Unknown;
            return false;
        }

        private void Awake()
        {
            if (!ValidateAuthoredSources(out string error))
            {
                Debug.LogError("[QuestFlight][OVRInput] " + error, this);
                enabled = false;
            }
        }

        private void Start()
        {
            // All official Meta interactors have completed Awake before Start.
            BuildLookup();
        }

        private void BuildLookup()
        {
            _byIdentifier.Clear();
            if (sources == null) return;
            for (int i = 0; i < sources.Length; i++)
            {
                Source source = sources[i];
                if (source.interactor is IInteractorView view)
                    _byIdentifier[view.Identifier] = source;
            }
        }
    }
}
