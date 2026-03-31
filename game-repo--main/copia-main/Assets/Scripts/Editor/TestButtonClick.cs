#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public static class TestButtonClick
{
    public static void Execute()
    {
        var canvas = GameObject.Find("Canvas");
        if (canvas == null) { Debug.LogError("No Canvas"); return; }

        var btnGo = canvas.transform.Find("ConnectWalletButton");
        if (btnGo == null) { Debug.LogError("No ConnectWalletButton"); return; }

        var btn = btnGo.GetComponent<Button>();
        if (btn == null) { Debug.LogError("No Button component"); return; }

        Debug.Log($"[TestButton] Button found. interactable={btn.interactable}, gameObject.active={btnGo.gameObject.activeInHierarchy}");
        Debug.Log($"[TestButton] targetGraphic={(btn.targetGraphic != null ? btn.targetGraphic.name : "NULL")}");
        Debug.Log($"[TestButton] Image raycastTarget={btnGo.GetComponent<Image>()?.raycastTarget}");
        Debug.Log($"[TestButton] Layer={btnGo.gameObject.layer} ({LayerMask.LayerToName(btnGo.gameObject.layer)})");

        // Check EventSystem
        var es = EventSystem.current;
        Debug.Log($"[TestButton] EventSystem={(es != null ? es.name : "NULL")}");

        // Check GraphicRaycaster
        var raycaster = canvas.GetComponent<GraphicRaycaster>();
        Debug.Log($"[TestButton] GraphicRaycaster={(raycaster != null ? "present, enabled=" + raycaster.enabled : "MISSING")}");

        // Check if mana component exists and has button ref
        var mana = canvas.GetComponent<MonoBehaviour>();
        Debug.Log($"[TestButton] mana component on Canvas: {mana != null}");

        Debug.Log("[TestButton] All checks done. If button still doesn't respond, check if OnGUI is eating events.");
    }
}
#endif
