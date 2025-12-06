using UnityEngine;
using System.Collections;
using System.Linq;

public class WordDrawer : Panel {
    public void Start() {
        SetupPanel();
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        animationMaster = stageObject.GetComponent<AnimationMaster>();
        scaffolder = stageObject.GetComponent<Scaffolder>();
        environment = stageObject.GetComponent<Environment>();
        scroller = transform.Find("scroller").gameObject;
        wordBox = GameObject.FindWithTag("WordBox").GetComponent<WordBox>();
        GetStagingArea().transform.localPosition = Vector3.zero;
    }

    public void ConfigureUI()
    {
        bool isChildDriven = environment.GetUser().InChildDrivenCondition();
        GetWordBank().SetActive(isChildDriven);
        GetStagingArea().SetActive(!isChildDriven);
    }

    public override string GetSortingLayer()
    {
        return "word_drawer";
    }

    public GameObject GetKeyboard()
    {
        return transform.Find("scroller").Find("keyboard-area").gameObject;
    }

    public GameObject GetWordBank()
    {
        return transform.Find("scroller").Find("word_bank_area").gameObject;
    }

    public GameObject GetStagingArea()
    {
        return transform.Find("scroller").Find("staging_area").gameObject;
    }

    public void InvokeKeyboard(bool instant)
    {
        Vector3 target = new Vector3(KEYBOARD_X, 0, 0);
        if (instant)
        {
            scroller.transform.localPosition = target;
        }
        else
        {
            animationMaster.StartLocalGlide(scroller, target, 0.5f);
        }
    }

    public void InvokeWordBank(bool instant)
    {
        Vector3 target = new Vector3(0, 0, 0);
        if (instant)
        {
            scroller.transform.localPosition = target;
        }
        else
        {
            animationMaster.StartLocalGlide(scroller, target, 0.5f);
        }
    }

    public bool IsDisplayingWordBank()
    {
        return Mathf.Abs(scroller.transform.localPosition.x) < 1e-6;
    }

    public bool IsDisplayingKeyboard()
    {
        return Mathf.Abs(scroller.transform.localPosition.x - KEYBOARD_X) < 1e-6;
    }

    protected override void OnRetract()
    {
        if (null != scaffolder.GetTarget())
        {
            if (scaffolder.IsComplete())
            {
                scaffolder.UnsetTarget();
                wordBox.InstantClear();
                GameObject[] writeButtons = GameObject.FindGameObjectsWithTag("WriteButton");
                foreach (GameObject writeButton in writeButtons)
                {
                    if ("word_drawer" == ZSorting.GetSortingLayer(writeButton))
                    {
                        Destroy(writeButton);
                    }
                }
            }
            else
            {
                scaffolder.InterruptCurrentProcess();
            }
            InvokeWordBank(instant: true);
        }
    }


    public void SlideWordDrawerOutOfView(bool instant = false)
    {

        OnRetract();
        Vector3 targetPosition = new Vector3(transform.localPosition.x, -10f, -10f);

        if (instant)
        {
            transform.localPosition = targetPosition;
        }
        else
        {
            if (animationMaster != null)
            {
                animationMaster.StartLocalGlide(gameObject, targetPosition, 0.5f);
            }
            else
            {
                // fallback if animationMaster is missing
                transform.localPosition = targetPosition;
            }
        }

    }


    public const float DEPLOYMENT_TIME = 0.25f;

    private Environment environment;
    private GameObject scroller = null;
    private WordBox wordBox = null;
    private AnimationMaster animationMaster = null;
    private Scaffolder scaffolder = null;
    private const float KEYBOARD_X = -17;
}
