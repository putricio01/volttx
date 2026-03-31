#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class SetPanelsInactive
{
    public static void Execute()
    {
        var canvas = GameObject.Find("Canvas");
        if (canvas == null) { Debug.LogError("Canvas not found"); return; }

        var board = canvas.transform.Find("ChallengeBoardPanel");
        if (board != null)
        {
            board.gameObject.SetActive(false);
            EditorUtility.SetDirty(board.gameObject);
            Debug.Log("ChallengeBoardPanel set inactive");
        }

        var loading = canvas.transform.Find("MatchLoadingPanel");
        if (loading != null)
        {
            loading.gameObject.SetActive(false);
            EditorUtility.SetDirty(loading.gameObject);
            Debug.Log("MatchLoadingPanel set inactive");
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}
#endif
