using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Visual ball trail effect — client only. Server has no rendering.
/// Disables itself on server to avoid unnecessary work.
/// Uses position deltas to compute speed since client ball is kinematic
/// (NetworkRigidbody sets it kinematic, so linearVelocity is always zero).
/// </summary>
public class BallTrail : NetworkBehaviour
{
    public float trailTime = 0.7f;

    float _speedMagnitude = 0, _speedMagnitudeKmH = 0;
    Rigidbody _rb;
    TrailRenderer _trail;
    Gradient _gradient;
    Vector3 _lastPosition;

    public override void OnNetworkSpawn()
    {
        // Server doesn't need visual trail effects
        if (IsServer && !IsHost)
        {
            enabled = false;
            return;
        }
    }

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _trail = GetComponent<TrailRenderer>();
        if (_trail == null) { enabled = false; return; }
        _trail.time = trailTime;
        _lastPosition = transform.position;

        _gradient = new Gradient();
        _gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0.0f), new GradientAlphaKey(0, 1.0f) }
        );
    }

    private void FixedUpdate()
    {
        if (_rb == null || _trail == null || _gradient == null) return;

        // On server or host, Rigidbody velocity is accurate
        // On client, ball is kinematic so compute speed from position delta
        if (_rb.isKinematic)
        {
            _speedMagnitude = (transform.position - _lastPosition).magnitude / Time.fixedDeltaTime;
            _lastPosition = transform.position;
        }
        else
        {
            _speedMagnitude = _rb.linearVelocity.magnitude;
        }
        _speedMagnitudeKmH = _speedMagnitude * 3.6f;

        var currentAlpha = RoboUtils.Scale(40, 100, 0, 1, _speedMagnitudeKmH);
        currentAlpha = Mathf.Clamp(currentAlpha, 0, 1);
        _gradient.alphaKeys = new GradientAlphaKey[]{ new GradientAlphaKey(currentAlpha, 0), new GradientAlphaKey(0, 1) };
        _trail.colorGradient = _gradient;

        var maxTrailWidth = 0.6f;
        var minTrailWidth = 0.3f;
        var trailWidth = RoboUtils.Scale(40, 80, minTrailWidth, maxTrailWidth, _speedMagnitudeKmH);
        _trail.startWidth = Mathf.Clamp(trailWidth, minTrailWidth, maxTrailWidth);
    }
}