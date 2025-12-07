using UnityEngine;
using System.Collections;

public class TurnSwitcherButton : MonoBehaviour
{
    public Sprite yourTurnSprite;
    public Sprite jiboTurnSprite;

    private SpriteRenderer sr;
    private bool isYourTurn = true;

    private CoCreateButton coCreate;

    private void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        coCreate = FindObjectOfType<CoCreateButton>();

        sr.sprite = yourTurnSprite;
    }

    private void OnMouseDown()
    {
        if (coCreate == null)
        {
            Debug.LogWarning("[TurnSwitcherButton] No CoCreateButton found!");
            return;
        }

        if (isYourTurn)
        {
            StartCoroutine(JiboTurnSequence());
        }
    }

    private IEnumerator JiboTurnSequence()
    {
        // Switch to Jibo turn sprite
        sr.sprite = jiboTurnSprite;
        isYourTurn = false;

        // Trigger the regular spawn picture, with a callback for when Jibo finishes
        coCreate.OnTap(() =>
        {
            // Switch back to player's turn
            sr.sprite = yourTurnSprite;
            isYourTurn = true;
        });

        yield break;
    }
}