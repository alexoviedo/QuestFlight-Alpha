using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.V2
{
    [DefaultExecutionOrder(CockpitSystemsExecutionOrder.Presentation)]
    [DisallowMultipleComponent]
    public sealed class CockpitMagneticCompassController : MonoBehaviour
    {
        private const float ResponseSeconds = 0.45f;
        private static readonly int HeadingId =
            Shader.PropertyToID("_HeadingDegrees");

        [SerializeField] private MonoBehaviour authorityProvider;
        [SerializeField] private MeshRenderer cardRenderer;
        [SerializeField] private float authoredHeadingDegrees = 330f;

        private ICockpitInstrumentV2AuthorityProvider _authority;
        private MaterialPropertyBlock _properties;
        private float _heading;
        private float _velocity;
        private bool _hasHeading;

        public void ConfigureAuthoredReferences(
            MonoBehaviour provider,
            MeshRenderer renderer,
            float authoredHeading)
        {
            authorityProvider = provider;
            cardRenderer = renderer;
            authoredHeadingDegrees = authoredHeading;
        }

        private void Awake()
        {
            _authority = authorityProvider as ICockpitInstrumentV2AuthorityProvider;
            _properties = new MaterialPropertyBlock();
            ApplyHeading(authoredHeadingDegrees);
            if (_authority == null || cardRenderer == null)
            {
                Debug.LogError(
                    "[QuestFlight][MagneticCompass] Authored references are incomplete.",
                    this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (_authority == null ||
                !_authority.TryGetInstrumentV2Snapshot(out var snapshot) ||
                !CockpitInstrumentV2Kinematics.TryEvaluate(
                    CockpitInstrumentV2Function.MagneticCompassHeadingDegrees,
                    snapshot,
                    out float target))
            {
                _hasHeading = false;
                ApplyHeading(authoredHeadingDegrees);
                return;
            }

            if (!_hasHeading)
            {
                _heading = target;
                _velocity = 0f;
                _hasHeading = true;
            }
            else
            {
                _heading = Mathf.SmoothDampAngle(
                    _heading,
                    target,
                    ref _velocity,
                    ResponseSeconds,
                    Mathf.Infinity,
                    Time.deltaTime);
            }
            ApplyHeading(_heading);
        }

        private void ApplyHeading(float heading)
        {
            if (cardRenderer == null) return;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            cardRenderer.GetPropertyBlock(_properties);
            _properties.SetFloat(
                HeadingId,
                CockpitInstrumentV2Kinematics.NormalizeHeadingDegrees(heading));
            cardRenderer.SetPropertyBlock(_properties);
        }
    }
}
