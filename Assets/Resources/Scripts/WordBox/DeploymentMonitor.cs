using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeploymentMonitor : MonoBehaviour
{
    private List<Action> callbacks = new List<Action>();

    void Start()
    {
        StartCoroutine(DeploymentMonitorCoroutine());
    }

    public static void Deploy(GameObject gameObject)
    {
        Debug.Log("Deploying");
        gameObject.transform.SetParent(GameObject.FindWithTag("CompositionRoot").transform);
        Logging.LogParent(gameObject, "deploy");
        GameObject.FindWithTag("WordDrawer").GetComponent<WordDrawer>().Retract(retractInstantly: false);
    }

    public void AddCallback(Action callback)
    {
        callbacks.Add(callback);
    }

    private IEnumerator DeploymentMonitorCoroutine()
    {
        Draggable draggable = GetComponent<Draggable>();
        while (!draggable.IsTouched()) yield return null;
        Deploy(gameObject);
        yield return new WaitForSeconds(WordDrawer.DEPLOYMENT_TIME + 0.1f);
        WordDrawer wordDrawer = GameObject.FindWithTag("WordDrawer").GetComponent<WordDrawer>();
        ZSorting.SetSortingLayer(gameObject, "Default");
        draggable.PutOnTop();
        foreach (Action callback in callbacks) {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                ExceptionUtil.OnException(e);
            }
        }
        Destroy(this);
    }

    public void ForceDeploy()

        // Regular Deploy But Doesn't Require Touch
    {
        StopAllCoroutines();                    
        StartCoroutine(ForceDeployRoutine());   
    }

    private IEnumerator ForceDeployRoutine()
    {
        DeploymentMonitor.Deploy(gameObject);

        yield return new WaitForSeconds(WordDrawer.DEPLOYMENT_TIME + 0.1f);

        Draggable draggable = GetComponent<Draggable>();
        if (draggable != null)
            draggable.PutOnTop();

        ZSorting.SetSortingLayer(gameObject, "Default");

        
        foreach (var cb in callbacks)
            cb();

        Destroy(this);
    }
}
