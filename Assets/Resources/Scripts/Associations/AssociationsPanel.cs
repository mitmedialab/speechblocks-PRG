using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AssociationsPanel : MonoBehaviour {
    [SerializeField]
    private bool deployToTheRight = true;

    private List<AssociationButton> buttons = new List<AssociationButton>();
    private AssociationLibrary assocLib = null;
    private AnimationMaster animaster = null;
    private GameObject associationButtonPrefab = null;
    private Coroutine currentCoroutine;
    private Vector3 SMALL = new Vector3(0.11f, 0.11f, 1);
    private Vector3 NORMAL = new Vector3(1f, 1f, 1);
    private Vector3 LARGE = new Vector3(1.4f, 1.4f, 1);
    private GameObject writeButton = null;
    private GameObject ideaButton = null;
    private GameObject closeButton = null;
    private AssociationButton recentButton = null;
    private SynthesizerController synthesizer = null;
    private Environment environment;
    private StageOrchestrator stageOrchestrator;
    private string currentWordSense;
    private bool associationsEverInvoked = false;

    private float BUTTON_SPACING;
    private const double MAX_CANVAS_WAIT_TIME = 10;

    private List<WordSuggestion> currentSuggestions = new List<WordSuggestion>();

    public void Start() {
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        assocLib = stageObject.GetComponent<AssociationLibrary>();
        animaster = stageObject.GetComponent<AnimationMaster>();
        synthesizer = stageObject.GetComponent<SynthesizerController>();
        environment = stageObject.GetComponent<Environment>();
        stageOrchestrator = stageObject.GetComponent<StageOrchestrator>();
        associationButtonPrefab = Resources.Load<GameObject>("Prefabs/Association-Button");
        writeButton = transform.Find("write_button_holder").gameObject;
        ideaButton = transform.Find("idea_button_holder").gameObject;
        closeButton = transform.Find("close_button_holder").gameObject;
        writeButton.SetActive(false);
        ideaButton.SetActive(false);
        closeButton.SetActive(false);
        BUTTON_SPACING = 1.25f * associationButtonPrefab.GetComponent<AssociationButton>().GetButtonSize();


        currentSuggestions = new List<WordSuggestion>
        {
            new WordSuggestion("cat", "default"),
            new WordSuggestion("sun", "default"),
            new WordSuggestion("tree", "default")
        };
    }

    public void Invoke(string word_sense, string cause) {
        currentSuggestions = new List<WordSuggestion> { new WordSuggestion(word_sense, null) };
        associationsEverInvoked = true;
        if (null != currentCoroutine) { StopCoroutine(currentCoroutine); }
        currentCoroutine = StartCoroutine(InvocationCoroutine(word_sense, cause));
    }

    public void Invoke(List<WordSuggestion> suggestions, string cause)
    {
        currentSuggestions = suggestions;
        if (null != currentCoroutine) { StopCoroutine(currentCoroutine); }
        currentCoroutine = StartCoroutine(InvocationCoroutine(suggestions, cause));
    }

    public bool IsDeployed()
    {
        return 0 != buttons.Count;
    }

    public bool IsBeingDeployed()
    {
        return null != currentCoroutine;
    }

    public bool AssociationEverInvoked()
    {
        return associationsEverInvoked;
    }

    public void InvokeSelected()
    {
        if (null == recentButton) return;
        Invoke(recentButton.GetWordSense(), cause: "assoc-invoke-selected");
    }

    public string GetCurrentWordSense()
    {
        return currentWordSense;
    }

    public bool Select(AssociationButton invokingButton) {
        if (recentButton == invokingButton) return false;
        recentButton = invokingButton;
        writeButton.transform.localPosition = new Vector3(invokingButton.transform.localPosition.x, -BUTTON_SPACING, 0);
        writeButton.SetActive(true);
        writeButton.transform.localScale = SMALL;
        animaster.StartScale(writeButton, NORMAL, 0.25f);
        if (HasAssociations(invokingButton.GetWordSense()))
        {
            ideaButton.transform.localPosition = new Vector3(invokingButton.transform.localPosition.x, -2 * BUTTON_SPACING, 0);
            ideaButton.SetActive(true);
            ideaButton.transform.localScale = SMALL;
            animaster.StartScale(ideaButton, NORMAL, 0.25f);
        }
        else
        {
            ideaButton.SetActive(false);
        }
        return true;
    }

    public void Spell() {
        if (null != recentButton)
        {
            GameObject.FindWithTag("StageObject").GetComponent<Scaffolder>().SetTarget(recentButton.GetWordSense(), cause: "associations");
            WordDrawer wordDrawer = GameObject.FindWithTag("WordDrawer").GetComponent<WordDrawer>();
            wordDrawer.InvokeKeyboard(instant: true);
            wordDrawer.Deploy(deployInstantly: false);
        }
        Retract();
    }

    public void Retract() {
        if (null != currentCoroutine) { StopCoroutine(currentCoroutine); }
        currentCoroutine = StartCoroutine(RetractCoroutine());
    }

    public AssociationButton GetSelectedButton()
    {
        return recentButton;
    }

    public List<AssociationButton> GetAssociationButtons()
    {
        return buttons;
    }

    public bool HasAssociations(string word_sense)
    {
        return assocLib.GetAssociations(word_sense).Count > 0;
    }

    public void GetAttentionFocus(List<GameObject> buffer, string stage)
    {
        if (0 != buttons.Count && "canvas" == stage)
        {
            if (null != recentButton) { buffer.Add(recentButton.gameObject); }
            else { buffer.Add(buttons[buttons.Count - 1].gameObject); }
        }
    }

    private IEnumerator InvocationCoroutine(string word_sense, string cause) {
        currentWordSense = word_sense;
        environment.GetRoboPartner().LookAtTablet();
        ClearButtons();
        List<string> associations = assocLib.GetAssociations(word_sense);
        AnnounceAssociations(word_sense, associations, cause);
        yield return DeployButtonsCoroutine(associations.Select(association => new WordSuggestion(association, reason: null)).ToList());
        currentCoroutine = null;
    }

    private IEnumerator InvocationCoroutine(List<WordSuggestion> suggestions, string cause)
    {
        currentWordSense = null;
        environment.GetRoboPartner().LookAtTablet();
        ClearButtons();
        yield return AwaitCanvas();
        if ("canvas" != stageOrchestrator.GetStage()) yield break;
        AnnounceWordList(cause);
        yield return DeployButtonsCoroutine(suggestions);
        currentCoroutine = null;
    }

    private IEnumerator DeployButtonsCoroutine(List<WordSuggestion> suggestions)
    {
        if (suggestions.Count > 7)
        {
            suggestions = suggestions.Take(7).ToList();
        }
        for (int i = suggestions.Count - 1; i >= 0; --i)
        {
            GameObject buttonObject = Instantiate(associationButtonPrefab);
            AssociationButton button = buttonObject.GetComponent<AssociationButton>();
            try
            {
                button.Setup(suggestions[i], buttons.Count);
                buttonObject.transform.SetParent(transform);
                buttonObject.transform.localPosition = GetButtonPosition(buttons.Count());
                buttonObject.transform.localScale = SMALL;
                animaster.StartScale(buttonObject, NORMAL, 0.25f);
                buttons.Add(button);
            }
            catch
            {
                Debug.Log("ISSUE WITH ASSOCIATION BUTTON " + suggestions[i].GetWordSense());
                Destroy(button.gameObject);
            }
            yield return null;
        }
        closeButton.SetActive(true);
        closeButton.transform.localPosition = GetButtonPosition(buttons.Count);
        closeButton.transform.localScale = SMALL;
        animaster.StartScale(closeButton, NORMAL, 0.25f);
        yield return new WaitForSeconds(0.25f);
    }

    private IEnumerator RetractCoroutine()
    {
        currentWordSense = null;
        writeButton.SetActive(false);
        ideaButton.SetActive(false);
        foreach (AssociationButton button in buttons)
        {
            animaster.StartScale(button.gameObject, SMALL, 0.25f);
        }
        animaster.StartScale(writeButton, SMALL, 0.25f);
        animaster.StartScale(closeButton, SMALL, 0.25f);
        yield return new WaitForSeconds(0.25f);
        foreach (AssociationButton button in buttons)
        {
            Destroy(button.gameObject);
        }
        buttons.Clear();
        closeButton.SetActive(false);
        currentCoroutine = null;
    }

    private Vector3 GetButtonPosition(int buttonI)
    {
        int sign = deployToTheRight ? 1 : -1;
        return new Vector3(BUTTON_SPACING * sign * (buttonI + 0.5f), 0, 0);
    }

    private void ClearButtons()
    {
        for (int i = 0; i < buttons.Count; ++i)
        {
            if (null == buttons[i]) continue;
            Destroy(buttons[i].gameObject);
        }
        closeButton.SetActive(false);
        writeButton.SetActive(false);
        ideaButton.SetActive(false);
        buttons.Clear();
    }

    private void AnnounceAssociations(string word_sense, List<string> associations, string cause)
    {
        string prompt;
        if (0 == associations.Count)
        {
            string[] unfortunately = { "Unfortunately", "Sadly", "Alas" };
            string[] iDontKnowWhatToAddTo = { "I don't know what to add to", "I can't think of something to add to" };
            prompt = $"{RandomUtil.PickOne("assoc1", unfortunately)} {RandomUtil.PickOne("assoc2", iDontKnowWhatToAddTo)} {Vocab.GetWord(word_sense)}";
        }
        else
        {
            string[] theseAre = { "These are", "Here are" };
            string[] aFew = { "a few", "some", "several" };
            string[] words = { "words", "things" };
            string[] that = { "that", "which" };
            string[] relatedTo = { "come to mind when I think about", "might go well with" };
            prompt = $"{RandomUtil.PickOne("assoc3", theseAre)} {RandomUtil.PickOne("assoc4", aFew)} {RandomUtil.PickOne("assoc5", words)} {RandomUtil.PickOne("assoc6", that)} {RandomUtil.PickOne("assoc7", relatedTo)} {Vocab.GetWord(word_sense)}";
        }
        synthesizer.Speak(prompt, cause: cause);
    }

    private IEnumerator AwaitCanvas()
    {
        double t0 = TimeKeeper.time;
        while (stageOrchestrator.GetStage() != "canvas" && TimeKeeper.time - t0 < MAX_CANVAS_WAIT_TIME) yield return null;
    }

    private void AnnounceWordList(string cause)
    {
        string prompt;
        string[] theseAre = { "These are", "Here are" };
        string[] aFew = { "a few", "some", "several" };
        string[] words = { "words", "things" };
        string[] that = { "that", "which" };
        string[] related = { "could fit", "might be useful" };
        prompt = $"{RandomUtil.PickOne("assoc-b3", theseAre)} {RandomUtil.PickOne("assoc-b4", aFew)} {RandomUtil.PickOne("assoc-b5", words)} {RandomUtil.PickOne("assoc-b6", that)} {RandomUtil.PickOne("assoc-b7", related)}.";
        synthesizer.Speak(prompt, cause: cause);
    }

    public List<WordSuggestion> GetSuggestionsForWord(string wordSense)
    {
        if (assocLib == null || string.IsNullOrEmpty(wordSense)) return new List<WordSuggestion>();

        List<string> assocWords = assocLib.GetAssociations(wordSense);
        if (assocWords.Count == 0) return new List<WordSuggestion>();

        return assocWords.Select(w => new WordSuggestion(w, null)).ToList();
    }
}
