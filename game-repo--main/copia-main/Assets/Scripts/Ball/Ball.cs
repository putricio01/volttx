﻿ using System;
using UnityEngine;
using Random = UnityEngine.Random;
using Unity.Netcode;

public class Ball : NetworkBehaviour
{
    [SerializeField] [Range(10,80)] float randomSpeed = 40;
    [SerializeField] float initialForce = 400;
    [SerializeField] float hitMultiplier = 50;

    private bool isTouchedGround = false;
    
    Rigidbody _rb;
    Transform _transform;
    
    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _transform = this.transform;
        isTouchedGround = false;
    }
    
    void Update()
    {
        //TODO: move inputs to the InputController
        if (Input.GetKeyDown(KeyCode.T))
            ShootInRandomDirection(randomSpeed);
        
        if (Input.GetKeyDown(KeyCode.R))
            ResetBall();
        
        if (Input.GetButtonDown("Select"))
            ResetShot(new Vector3(7.76f, 2.98f, 0f));
    }
    
    private void ResetShot(Vector3 pos)
    {
        _transform.position = pos;
        _rb.velocity = new Vector3(30, 10, 0);
        _rb.angularVelocity = Vector3.zero;
    }

    [ContextMenu("ResetBall")]
    public void ResetBall()
    {
        var desired = new Vector3(0, 12.23f, 0f);
        _transform.SetPositionAndRotation(desired, Quaternion.identity);
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    [ContextMenu("ShootInRandomDirection")]
    private void ShootInRandomDirection(float speed)
    {
        float speedRange = Random.Range(speed - 10, speed + 10);
        var randomDirection = Random.insideUnitCircle.normalized;
        var direction = new Vector3(randomDirection.x, Random.Range(-0.5f, 0.5f), randomDirection.y).normalized;
        _rb.velocity = direction * speedRange;
    }

 

// This method is called on the client and then calls the ServerRpc
[ServerRpc(RequireOwnership = false)]
    void RequestOwnershipServerRpc(ulong newOwnerClientId)
    {
        var networkObject = GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.ChangeOwnership(newOwnerClientId);
        }
    }

   private void OnCollisionEnter(Collision col)
{
    Rigidbody colRb = col.rigidbody;

    if (colRb != null)
    {
        float force = initialForce + colRb.velocity.magnitude * hitMultiplier;
        var dir = transform.position - col.transform.position;

        if (col.gameObject.CompareTag("Player"))
        {
            var networkObject = col.gameObject.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                RequestOwnershipServerRpc(networkObject.OwnerClientId);
            }

            _rb.AddForce(dir.normalized * force);
        }
    }

    if (col.gameObject.CompareTag("Ground"))
    {
        isTouchedGround = true;
    }
}

    //private void OnCollisionExit(Collision col)
    //{
    //    if (col.gameObject.CompareTag("Player"))
    //    {
    //        ResetOwnershipToHostServerRpc();
    //        print("ewew");
    //    }
    //}

    public bool Respawn()
    {
        // Set the position to the spawn point (you can hardcode it or reference a Transform)
        transform.position = new Vector3(0, 1, 0); // Example position
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        return true;
    }


    [ServerRpc(RequireOwnership = false)]
    public void ResetOwnershipToHostServerRpc()
    {
        // Check if the object has a NetworkObject component
        var networkObject = GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            // Change ownership back to the server/host
            networkObject.ChangeOwnership(NetworkManager.Singleton.LocalClientId);
        }
    }
}
