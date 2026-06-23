using UnityEngine;
using UnityEngine.UI;

public class HackPopupController : MonoBehaviour
{
    [SerializeField] private HackMinigame hackMinigame;
    [SerializeField] private M5VisualController visualController;

    void Start()
    {
        if (hackMinigame == null)
        {
            GameObject pm = GameObject.Find("PlaneManager") ?? GameObject.Find("PlaneManager ");
            if (pm != null)
            {
                hackMinigame = pm.GetComponent<HackMinigame>();
                visualController = pm.GetComponent<M5VisualController>();
            }
        }

        Button btn = GetComponentInChildren<Button>();
        if (btn != null)
            btn.onClick.AddListener(OnHackButtonClicked);
    }

    void OnHackButtonClicked()
    {
        if (hackMinigame != null && !hackMinigame.IsActive)
        {
            hackMinigame.StartHack();
            gameObject.SetActive(false);
        }
    }
}
