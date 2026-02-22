using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static AudioManager AudioManager;

    void Awake()
    {
        // AudioManager is client-only — safe to always try GetComponent
        // (will be null on server which is fine, callers should null-check)
        AudioManager = GetComponent<AudioManager>();
        //DontDestroyOnLoad(gameObject);
    }
}
