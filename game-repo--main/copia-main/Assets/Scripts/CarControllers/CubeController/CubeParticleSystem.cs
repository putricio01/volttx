using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Car particle/trail effects — purely visual, client-only.
/// Null-checks prevent errors on server where particles are disabled.
/// </summary>
public class CubeParticleSystem : MonoBehaviour
{
    public ParticleSystem windPs, boostPs;
    public GameObject firePs;

    const int SupersonicThreshold = 2200 / 100;
    CubeController _controller;
    InputManager _inputManager;
    private TrailRenderer[] _trails;
    bool _isBoostAnimationPlaying = false;

    void Start()
    {
        _controller = GetComponentInParent<CubeController>();
        _inputManager = GetComponentInParent<InputManager>();
        _trails = GetComponentsInChildren<TrailRenderer>();

        if (_trails == null || _trails.Length < 2 || windPs == null || boostPs == null)
        {
            enabled = false;
            return;
        }

        _trails[0].time = _trails[1].time = 0;
        if (firePs != null) firePs.SetActive(false);

        windPs.transform.position += new Vector3(0, 0, 10);
    }

    void Update()
    {
        if (_inputManager == null) return;
        if (_inputManager.isBoost)
        {
            if (_isBoostAnimationPlaying == false)
            {
                if (boostPs != null) boostPs.Play();
                if (firePs != null) firePs.SetActive(true);
                _isBoostAnimationPlaying = true;
            }
        }
        else if (!_inputManager.isBoost)
        {
            if (boostPs != null) boostPs.Stop();
            if (firePs != null) firePs.SetActive(false);
            _isBoostAnimationPlaying = false;
        }
    }

    const float TrailLength = 0.075f;

    private void FixedUpdate()
    {
        if (_controller == null || _trails == null || _trails.Length < 2) return;

        //  Wind and trail effect
        if (_controller.forwardSpeed >= SupersonicThreshold)
        {
            if (windPs != null) windPs.Play();

            if (_controller.isAllWheelsSurface)
                _trails[0].time = _trails[1].time = Mathf.Lerp(_trails[1].time, TrailLength, Time.fixedDeltaTime * 5);
            else
                _trails[0].time = _trails[1].time = 0;
        }
        else
        {
            if (windPs != null) windPs.Stop();

            _trails[0].time = _trails[1].time = Mathf.Lerp(_trails[1].time, 0.029f, Time.fixedDeltaTime * 6);
            if (_trails[0].time <= 0.03f)
                _trails[0].time = _trails[1].time = 0;
        }
    }
}
