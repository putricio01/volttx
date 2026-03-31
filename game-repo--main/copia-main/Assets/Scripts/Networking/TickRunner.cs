using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Replays car simulation sub-systems in an explicit order so rollback can
/// apply the same force/state pipeline that normally runs from FixedUpdate().
/// </summary>
[DisallowMultipleComponent]
public class TickRunner : MonoBehaviour
{
    readonly List<ITickSimulatable> _simulatables = new List<ITickSimulatable>(16);

    void Awake()
    {
        Refresh();
    }

    void OnEnable()
    {
        if (_simulatables.Count == 0)
            Refresh();
    }

    public void Refresh()
    {
        _simulatables.Clear();

        var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is ITickSimulatable simulatable)
                _simulatables.Add(simulatable);
        }

        _simulatables.Sort((a, b) => a.TickOrder.CompareTo(b.TickOrder));
    }

    public void RunPrePhysicsTick()
    {
        for (int i = 0; i < _simulatables.Count; i++)
        {
            var mb = _simulatables[i] as MonoBehaviour;
            if (mb == null)
                continue;

            if (!mb.enabled || !mb.gameObject.activeInHierarchy)
                continue;

            _simulatables[i].SimulateNetworkTick();
        }
    }
}
