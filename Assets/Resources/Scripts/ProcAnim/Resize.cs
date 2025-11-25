using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class Resize : MonoBehaviour, IAnimation {
    public static string UpdateType() { return "size"; }
    private float startX = 1;
    private float startY = 1;
    private float targetX = 1;
    private float targetY = 1;
    private float duration;
    private double startTime;
    private bool isGoing;
    private SpriteRenderer spriteRenderer = null;
    private Func<float, float> easing = null;

    public void Init(params object[] args)
    {
        //Debug.Log("INITIALIZING...");
        targetX = (float)args[0];
        targetY = (float)args[1];
        duration = (float)args[2];
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (args.Length > 3) { easing = (Func<float, float>)args[3]; }
        //Debug.Log("INITIALIZED");
    }

    public void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (spriteRenderer == null)
        {
            Debug.LogError("[Resize] No SpriteRenderer found, cannot resize!");
            isGoing = false;
            return;
        }

        isGoing = true;
        startTime = TimeKeeper.time;
        startX = spriteRenderer.size.x;
        startY = spriteRenderer.size.y;
    }
    public bool IsGoing()
    {
        return isGoing;
    }

    public void Stop()
    {
        //Debug.Log("STOPPED");
        Destroy(this);
    }
	
    // Update is called once per frame
    void Update()
    {
        if (!isGoing) return;
        float progress = Math.Min(1.0f, (float)(TimeKeeper.time - startTime) / duration);
        float progressE = (null != easing) ? easing(progress) : progress;
        //Debug.Log("PROGRESS: " + progress);
        float x = (1 - progress) * startX + progressE * targetX;
        float y = (1 - progress) * startY + progressE * targetY;
        spriteRenderer.size = new Vector2(x, y);
        if (1.0f <= progress)
        {
            isGoing = false;
            Destroy(this);
            //Debug.Log("FINISHED");
        }
    }
}
