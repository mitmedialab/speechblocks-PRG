using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WordBankButton : MonoBehaviour, ITappable, IDetailedLogging
{
    private string wordSense;
    private Vocab vocab;
    private string word;
    private static GameObject writeButtonPrefab = null;
    private WordDrawer wordDrawer;
    private SynthesizerController synthesizerHelper = null;
    private AnimationMaster animaster = null;
    private Environment environment;
    private CoCreateButton coCreateButton;
    private ResultBox resultBox;

    // Use this for initialization
    public void Setup(string wordSense)
    {
        if (null == writeButtonPrefab) { writeButtonPrefab = Resources.Load<GameObject>("Prefabs/WriteButton"); }
        wordDrawer = GameObject.FindWithTag("WordDrawer").GetComponent<WordDrawer>();
        this.wordSense = wordSense;
        word = Vocab.GetWord(wordSense);
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        synthesizerHelper = stageObject.GetComponent<SynthesizerController>();
        animaster = stageObject.GetComponent<AnimationMaster>();
        vocab = stageObject.GetComponent<Vocab>();
        environment = stageObject.GetComponent<Environment>();
        GetComponent<Picture>().Setup(wordSense, 0.9f, 0.9f, "word_drawer");

    }

    public string GetWord()
    {
        return word;
    }

    public string GetWordSense()
    {
        return wordSense;
    }

    public object[] GetLogDetails() {
        return new object[] { "word", word };
    }


    public void SpawnWordPicture(string wordSense)
    {
        GameObject prefab = Resources.Load<GameObject>("Prefabs/PictureBlock");
        if (prefab == null)
        {
            Debug.LogWarning("[WordBankButton] PictureBlock prefab not found!");
            return;
        }

        // Locate CoCreateButton and set seed word
        if (coCreateButton == null)
        {
            coCreateButton = GameObject.FindObjectOfType<CoCreateButton>();
            if (coCreateButton != null)
                coCreateButton.SetSeedWord(wordSense);
        }

        if (coCreateButton == null)
        {
            Debug.LogWarning("[WordBankButton] CoCreateButton reference missing!");
            return;
        }

        // Required for later saving/serialization
        Composition comp = GameObject.FindObjectOfType<Composition>();

        // Spawn position
        Vector3 spawnPos = coCreateButton.transform.position;
        spawnPos.z = -0.1f;

        // Instantiate picture block
        GameObject picture = Instantiate(prefab, spawnPos, Quaternion.identity);
        picture.name = "PictureBlock_" + wordSense;

        // Parent to CompositionRoot
        GameObject compositionRoot = GameObject.FindWithTag("CompositionRoot");
        if (compositionRoot != null)
            picture.transform.SetParent(compositionRoot.transform, true);

        // Setup PictureBlock component
        PictureBlock pb = picture.GetComponent<PictureBlock>();
        if (pb != null)
            pb.Setup(spawnPos, wordSense, "word_drawer");

        // Scale to match CoCreateButton
        float scale = picture.transform.localScale.x;
        float targetWidth = coCreateButton.GetComponent<SpriteRenderer>().size.x;
        scale = 0.95f * Mathf.Min(
            coCreateButton.GetComponent<SpriteRenderer>().size.y * scale / pb.GetHeight(),
            targetWidth * scale / pb.GetWidth()
        );
        picture.transform.localScale = new Vector3(scale, scale, 1);

       
        // Register with Composition
        if (comp != null)
            comp.RegisterPlacedPicture(picture);

        // Add DeploymentMonitor
        DeploymentMonitor deploymentMonitor = picture.AddComponent<DeploymentMonitor>();
        deploymentMonitor.AddCallback(() => Debug.Log("[WordBankButton] Picture deployed."));
        deploymentMonitor.ForceDeploy();

        // Scaffolder
        Scaffolder scaffolder = GameObject.FindObjectOfType<Scaffolder>();
        if (scaffolder != null)
        {
            scaffolder.SetTarget(wordSense, "SpawnWordPicture");
            if (!scaffolder.IsComplete())
                scaffolder.UnsetTarget();
        }
        else
        {
            Debug.LogWarning("[WordBankButton] Scaffolder not found.");
        }

        Debug.Log("[WordBankButton] Spawned picture block at CoCreateButton: " + wordSense);

    }

    public void OnTap(TouchInfo touchInfo) {
        environment.GetRoboPartner().LookAtTablet();
        GameObject[] writeButtons = GameObject.FindGameObjectsWithTag("WriteButton");
        foreach (GameObject wButton in writeButtons) { Destroy(wButton); }
        SynQuery synQuery = vocab.GetPronunciation(wordSense, giveFullNames: true);
        GameObject writeButton = Instantiate(writeButtonPrefab);
        writeButton.transform.SetParent(transform, false);
        writeButton.transform.localPosition = new Vector3(0, 0, -3);
        ZSorting.SetSortingLayer(writeButton, "word_drawer");
        //writeButton.GetComponent<WriteButton>().Setup(wordSense, () => wordDrawer.InvokeKeyboard(false), 0.8f, "word_drawer");
        writeButton.GetComponent<WriteButton>().Setup(
            wordSense,
            () =>
            {
                // Slide drawer out of view instead of invoking keyboard
                wordDrawer.SlideWordDrawerOutOfView();
                SpawnWordPicture(wordSense);

            },
            0.8f,
            "word_drawer"
        );


        Opacity.SetOpacity(writeButton, 0);
        animaster.StartFade(writeButton, 1, 0.25f);
        synthesizerHelper.Speak(synQuery, cause: Logging.GetObjectLogID(gameObject), keepPauses: false);
    }
}
