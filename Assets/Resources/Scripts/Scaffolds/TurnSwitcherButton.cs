using UnityEngine;
using System.Collections.Generic;
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
        if (isYourTurn)
        {
            // Switch to Jibo turn
            isYourTurn = false;
            sr.sprite = jiboTurnSprite;

            // Start Jibo's action
            StartCoroutine(JiboTurnSequence());
        }
    }

    private IEnumerator JiboTurnSequence()
    {
        
        sr.sprite = jiboTurnSprite;

        
        if (coCreate != null)
            yield return coCreate.TriggerCoCreateAndWait();

      
        isYourTurn = true;
        sr.sprite = yourTurnSprite;
    }

}