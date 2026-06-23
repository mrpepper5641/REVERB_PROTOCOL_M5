using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HackButtonCanvas の HackButton.onClick を HackMinigame.StartHack() に接続する
/// PlaneManager にアタッチするか、HackButtonCanvas 自体にアタッチして使用
/// </summary>
public class HackButtonConnector : MonoBehaviour
{
    [SerializeField] private HackMinigame hackMinigame;
    [SerializeField] private Button hackButton;

    void Start()
    {
        // 自動検索
        if (hackMinigame == null)
        {
            GameObject pm = GameObject.Find("PlaneManager") ?? GameObject.Find("PlaneManager ");
            if (pm != null) hackMinigame = pm.GetComponent<HackMinigame>();
        }

        if (hackButton == null)
        {
            GameObject btnGO = GameObject.Find("HackButton");
            if (btnGO != null) hackButton = btnGO.GetComponent<Button>();
        }

        if (hackButton != null && hackMinigame != null)
        {
            hackButton.onClick.AddListener(() =>
            {
                if (!hackMinigame.IsActive)
                    hackMinigame.StartHack();
            });
            Debug.Log("[HackButtonConnector] onClick 接続完了");
        }
        else
        {
            Debug.LogWarning("[HackButtonConnector] HackMinigame または HackButton が見つかりません");
        }
    }
}
