using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleController : MonoBehaviour
{
    [HideInInspector]
    public float velForward, velMagn;
    private Rigidbody _rb;
    void Start()
    {
        _rb = GetComponentInParent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        velForward = Vector3.Dot(_rb.linearVelocity, transform.forward);
        velForward = (float)Math.Round(velForward, 2);
        velMagn = _rb.linearVelocity.magnitude;
    }
}
