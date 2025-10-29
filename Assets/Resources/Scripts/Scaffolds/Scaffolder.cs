using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public class Scaffolder : MonoBehaviour
{
    [SerializeField]
    public GameObject helpButton = null;
    private GameObject[] customKeyboardKeys = null;

    private WordBox wordBox;
    private TouchManager touchManager;
    private AudioSource audioSource;
    private Syllabifier syllabifier;
    private Vocab vocab;
    private string targetWordSense = null;
    private SynQuery wordSynQuery = null;
    private PGMapping target = null;
    private int[] syllableBreakdown = null;
    private ResultBox resultBox = null;
    private bool isCompleted = true;

    private CoroutineRunner scaffoldProcessRunner = new CoroutineRunner();

    private KeysSchedule keysSchedule = null;
    private AnimationMaster animaster = null;
    private Environment environment;
    private SynthesizerController synthesizer;
    private SpeechAccessDispatcher speechAccessDispatcher;

    private KeyboardKey lastTappedKey = null;

    private int pgSlotWeAreWorkingOn = -1;
    private int scaffoldingLevelEmployed = 0;
    private int scaffoldingLevelTargeted = 0;
    private int correctionCount = 0;

    private bool[] completionMask = null;

    private List<IScaffoldLevel> scaffoldLevels = new List<IScaffoldLevel>();
    private IScaffolderDecisionModel scaffolderDecisionModel = null;

    private string[] ADD_PHRASES = { "Let me add a bit to this.", "Just a tiny thing to add", "Let me add just a little thing to this." };
    private string[] CHANGE_PHRASES = { "But let me change this a bit.", "But there is a tiny change to make.", "But I shall change this a bit." };
    private string[] CAN_YOU_DRAG_PHRASES = { "Can you drag it to the word box now?", "Let's drag it to the word box now!" };

    private const float GLIDE_DURATION = 0.5f;
    private const float CROSS_FADE_DURATION = 0.3f;
    private SynQuery BREAK = SynQuery.Break(0.25f);

    private int wboxUpdateCount = 0;

    private Tutorial tutorial;
    private bool helpButtonFlag = false;

    private int currentSpeechID = -1;

    private GameObject objectOfAttention = null;

    private int scaffolderTargetID = -1;
    private int scaffolderInteractionID = -1;

    public const int POSITION_TYPE_INITIAL = 0;
    public const int POSITION_TYPE_INITIAL_IN_SYLLABLE = 1;
    public const int POSITION_TYPE_FINAL = 2;
    public const int POSITION_TYPE_FINAL_IN_SYLLABLE = 3;
    public const int POSITION_TYPE_MEDIAL = 4;

    private GameObject glowPrefab = null;
    private GameObject glow;

    private VirtualJiboPartner jiboPartner;

    void Start()
    {
        glowPrefab = Resources.Load<GameObject>("Prefabs/glow");
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        audioSource = stageObject.GetComponent<AudioSource>();
        touchManager = stageObject.GetComponent<TouchManager>();
        vocab = stageObject.GetComponent<Vocab>();
        syllabifier = stageObject.GetComponent<Syllabifier>();
        tutorial = stageObject.GetComponent<Tutorial>();
        wordBox = GameObject.FindWithTag("WordBox").GetComponent<WordBox>();
        wordBox.AddUpdateProcessingCompletedCallback(() => OnWordBoxUpdate());
        resultBox = GameObject.FindWithTag("ResultBox").GetComponent<ResultBox>();
        GameObject letterSpace = GameObject.FindWithTag("LetterSpace");
        customKeyboardKeys = GameObject.FindGameObjectsWithTag("KeyboardKey").Where(obj => obj.name.StartsWith("custom-key")).ToArray();
        foreach (GameObject customKey in customKeyboardKeys) { customKey.SetActive(false); }
        environment = GetComponent<Environment>();
        synthesizer = GetComponent<SynthesizerController>();
        speechAccessDispatcher = GetComponent<SpeechAccessDispatcher>();
        animaster = GetComponent<AnimationMaster>();
        ScaffoldLevelSyllabic scaffoldLevelSyllabic = new ScaffoldLevelSyllabic();
        scaffoldLevels.Add(scaffoldLevelSyllabic);
        scaffoldLevels.Add(new ScaffoldLevelAnalogy(scaffoldLevelSyllabic));
        scaffoldLevels.Add(new ScaffoldLevelTargetSound(scaffoldLevelSyllabic));
        scaffoldLevels.Add(new ScaffoldLevelTargetBlock());
        scaffolderDecisionModel = new FixedDecisionModel();

        jiboPartner = GameObject.FindObjectOfType<VirtualJiboPartner>();
        if (jiboPartner == null)
        {
            Debug.LogWarning("Scaffolder: VirtualJiboPartner not found in scene. Jibo celebrations will be disabled.");
        }
    }

    void Update()
    {
        scaffoldProcessRunner.Update();
    }

    public void SetTarget(string targetWordSense, string cause) {
        PGMapping transcription = vocab.GetMapping(targetWordSense);
        SetTarget(targetWordSense, transcription, cause);
    }

    public void SetTarget(string targetWordSense, PGMapping target, string cause)
    {
        Clear();
        if (null == targetWordSense || null == target) return;
        ++scaffolderTargetID;
        scaffolderInteractionID = 0;
        isCompleted = false;
        this.targetWordSense = targetWordSense;
        this.target = target;
        Logging.LogScaffoldingTarget(scaffolderTargetID, target, cause);
        wordSynQuery = vocab.GetPronunciation(target.compositeWord);
        syllableBreakdown = syllabifier.Syllabify(target.compositeWord, target.pgs);
        Logging.LogScaffoldingSyllabification(scaffolderTargetID, target, syllableBreakdown);
        completionMask = Enumerable.Repeat<bool>(false, target.collapsedWord.Length).ToArray();
        helpButton.SetActive(true);
        AssembleKeyboard();
        wordBox.ActivateScaffolding();
        resultBox.Clear();
        pgSlotWeAreWorkingOn = -1;
        TransitionToPGSlot(0);
        scaffoldProcessRunner.SetCoroutine(PromptCoroutine(pgSlotWeAreWorkingOn, new List<SynQuery>()));
    }

    public void Reprompt()
    {
        InterruptCurrentProcess();
        scaffoldProcessRunner.SetCoroutine(PromptCoroutine(pgSlotWeAreWorkingOn, new List<SynQuery>()));
    }

    public int GetTargetID()
    {
        return scaffolderTargetID;
    }

    public void UnsetTarget() {
        if (!isCompleted && null != this.target) {
            Clear();
        } else {
            ResetKeyboard();
        }
        isCompleted = true;
        target = null;
        helpButton.SetActive(false);
        Unhighlight();
    }

    public string GetTargetWordSense()
    {
        return targetWordSense;
    }

    public PGMapping GetTarget() {
        return target;
    }

    public int TargetAt(int letterPos, bool strict) {
        for (int i = 0; i < target.pgs.Count; ++i) {
            PGPair pgPair = target.pgs[i];
            if (0 == letterPos || !strict && letterPos < pgPair.GetGrapheme().Length) return i;
            letterPos -= pgPair.GetGrapheme().Length;
        }
        return -1;
    }

    public void Help() {
        helpButtonFlag = true;
        if (scaffoldProcessRunner.IsRunning()) return;
        ++scaffolderInteractionID;
        Logging.LogScaffoldingInteraction(scaffolderTargetID, scaffolderInteractionID, "help");
        ElevateScaffoldingLevel();
        scaffoldProcessRunner.SetCoroutine(PromptCoroutine(pgSlotWeAreWorkingOn, new List<SynQuery>()));
    }

    public bool IsComplete() { return isCompleted; }

    public int GetFirstVacantPGSlot()
    {
        int letterIndex = 0;
        for (int i = 0; i < target.pgs.Count; ++i)
        {
            int intervalEnd = letterIndex + target.pgs[i].GetGrapheme().Length;
            if (wordBox.IntervalHasUnassignedCells(letterIndex, intervalEnd)) return i;
            letterIndex = intervalEnd;
        }
        return -1;
    }

    public int IthPGSlotStart(int i)
    {
        return target.pgs.Take(i).Sum(pg => pg.GetGrapheme().Length);
    }

    public int GetPGSlot(float letterPosition)
    {
        for (int i = 0; i < target.pgs.Count; ++i)
        {
            int ithGraphemeLen = target.pgs[i].GetGrapheme().Length;
            if (letterPosition < ithGraphemeLen) { return i; }
            letterPosition -= ithGraphemeLen;
        }
        return -1;
    }

    public int GetLandingPosition(PGPair pgPair, int intendedPGSlot)
    {
        PGPair targetPGPair = target.pgs[intendedPGSlot];
        Debug.Log($"Finding landing position for {pgPair.GetGrapheme()} in {intendedPGSlot}");
        int graphemeLandingPosition = FindGraphemeLandingPosition(pgPair.GetGrapheme(), intendedPGSlot);
        if (graphemeLandingPosition >= 0)
        {
            Debug.Log("Grapheme landing position found");
            return graphemeLandingPosition;
        }
        string[] targetBreakdown = PhonemeUtil.GetGraphemeBreakdown(targetPGPair.GetPhonemeCode(), targetPGPair.GetGrapheme());
        int pgSlotStart = IthPGSlotStart(intendedPGSlot);
        int coreStart = pgSlotStart + targetBreakdown[0].Length;
        int coreEnd = coreStart + targetBreakdown[1].Length;
        if (!wordBox.IntervalHasAssignedCells(coreStart, coreEnd) && HasPhoneticMatch(pgPair, intendedPGSlot))
        {
            Debug.Log("Phonetic landing position found");
            return coreStart;
        }
        Debug.Log("No landing position found");
        return -1;
    }

    public void RegisterKeyTap(KeyboardKey keyboardKey)
    {
        this.lastTappedKey = keyboardKey;
    }

    public void GetObjectsOfAttention(List<GameObject> objectsOfAttentionBuffer, string stage)
    {
        if ("keyboard" == stage && null != objectOfAttention)
        {
            objectsOfAttentionBuffer.Add(objectOfAttention);
        }
    }

    public void InterruptCurrentProcess()
    {
        if (scaffoldProcessRunner.IsRunning())
        {
            Debug.Log("Interrupting current process");
            scaffoldProcessRunner.Stop();
            synthesizer.InterruptSpeech(currentSpeechID);
            objectOfAttention = null;
            touchManager.Unconstrain();
        }
    }

    private void Clear() {
        if (!isCompleted && null != target) { Logging.LogScaffoldingCancel(scaffolderTargetID); }
        InterruptCurrentProcess();
        wordBox.InstantClear();
        resultBox.Clear();
        ResetKeyboard();
        Unhighlight();
    }

    private void ResetKeyboard()
    {
        foreach (GameObject customKey in customKeyboardKeys) { customKey.SetActive(false); }
        foreach (GameObject keyboardKeyObj in GameObject.FindGameObjectsWithTag("KeyboardKey"))
        {
            if (customKeyboardKeys.Contains(keyboardKeyObj)) continue;
            KeyboardKey keyboardKey = keyboardKeyObj.GetComponent<KeyboardKey>();
            keyboardKey.ChangePhonemecode(PhonemeUtil.GetDefaultPhoneme(keyboardKey.GetGrapheme()), cause: "scaf-reset");
            keyboardKey.Activate();
        }
    }

    private void BuildKeysSchedule()
    {
        KeysSchedule schedule = new KeysSchedule();
        for (int i = 0; i < target.pgs.Count; ++i)
        {
            PGPair pgPair = target.pgs[i];
            string phonemecode = pgPair.GetUnaccentuatedPhonemeCode();
            string grapheme = pgPair.GetGrapheme();
            string[] grapheme_breakdown = PhonemeUtil.GetGraphemeBreakdown(phonemecode, grapheme, useDefault: false);
            if (null != grapheme_breakdown)
            {
                string core_grapheme = grapheme_breakdown[1];
                schedule.AddKeyState(core_grapheme, phonemecode, i, is_aux: false);
            }
            string default_grapheme = PhonemeUtil.GetDefaultGrapheme(phonemecode);
            if (null != default_grapheme)
            {
                schedule.AddKeyState(default_grapheme, phonemecode, i, is_aux: true);
            }
            List<string> plausibleSpellings = PhonemeUtil.GetPlausibleSpellings(phonemecode);
            for (int j = 1; j < plausibleSpellings.Count; ++j)
            {
                if (1 != plausibleSpellings[j].Length) continue;
                schedule.AddKeyState(plausibleSpellings[j], PhonemeUtil.GetDefaultPhoneme(plausibleSpellings[j]), i, is_aux: true);
            }
            List<string> core_grs_for_ph = PhonemeUtil.GetCoreGraphemes(phonemecode);
            for (int j = 0; j < grapheme.Length; ++j)
            {
                string letterKey = grapheme.Substring(j, 1);
                string addUnderPhoneme = (null != core_grs_for_ph && core_grs_for_ph.Contains(letterKey)) ? phonemecode : PhonemeUtil.GetDefaultPhoneme(letterKey);
                schedule.AddKeyState(letterKey, addUnderPhoneme, i, is_aux: true);
            }
        }
        this.keysSchedule = schedule;
    }

    private void ActivateKeys(int mapping_position)
    {
        ActivateLetterKeys(mapping_position);
        ActivateCustomKeys(mapping_position);
    }

    private void ActivateLetterKeys(int mapping_position)
    {
        foreach (GameObject keyboardKeyObject in GameObject.FindGameObjectsWithTag("KeyboardKey"))
        {
            if (customKeyboardKeys.Contains(keyboardKeyObject)) continue;
            KeyboardKey keyboardKey = keyboardKeyObject.GetComponent<KeyboardKey>();
            string keyGrapheme = keyboardKey.GetGrapheme();
            string phoneme_target = keysSchedule.YieldPhoneme(keyGrapheme, mapping_position);
            if (null != phoneme_target)
            {
                keyboardKey.Setup(new PGPair(phoneme_target, keyGrapheme), "scaffolder");
                keyboardKey.Activate();
            }
            else
            {
                keyboardKey.Deactivate();
            }
        }
    }

    private void ActivateCustomKeys(int mapping_position)
    {
        List<string> multiletter_graphemes = keysSchedule.GetActiveMultiletterGraphemes(mapping_position, customKeyboardKeys.Length);
        foreach (GameObject customKeyObj in customKeyboardKeys)
        {
            if (!customKeyObj.activeSelf) continue;
            KeyboardKey customKey = customKeyObj.GetComponent<KeyboardKey>();
            if (multiletter_graphemes.Contains(customKey.GetGrapheme()))
            {
                multiletter_graphemes.Remove(customKey.GetGrapheme());
            }
            else
            {
                customKeyObj.SetActive(false);
            }
        }
        List<GameObject> inactiveKeys = customKeyboardKeys.Where((key) => !key.activeSelf).OrderBy(key => -key.transform.position.x).ToList();
        foreach (string grapheme in multiletter_graphemes)
        {
            string phonemecode = keysSchedule.YieldPhoneme(grapheme, mapping_position);
            GameObject keyPick = inactiveKeys[inactiveKeys.Count - 1];
            KeyboardKey keyboardKey = keyPick.GetComponent<KeyboardKey>();
            keyPick.SetActive(true);
            keyboardKey.Setup(new PGPair(phonemecode, grapheme), "scaffolder");
        }
    }

    private void AssembleKeyboard() {
        BuildKeysSchedule();
        ActivateKeys(0);
    }

    private void TransitionToPGSlot(int targetPGSlot)
    {
        if (pgSlotWeAreWorkingOn == targetPGSlot) return;
        pgSlotWeAreWorkingOn = targetPGSlot;
        scaffoldingLevelEmployed = 0;
        scaffoldingLevelTargeted = scaffolderDecisionModel.DecideScaffoldingLevel(target.pgs[targetPGSlot], GetPositionType(targetPGSlot));
        ActivateKeys(targetPGSlot);
    }

    private void OnWordBoxUpdate()
    {
      
        Debug.Log("SCAFFOLDER: Word box update");
        Unhighlight();


        if (null == target || isCompleted) return;

        ++wboxUpdateCount;
        ++scaffolderInteractionID;
        Logging.LogScaffoldingInteraction(scaffolderTargetID, scaffolderInteractionID, "wbox-update");
        InterruptCurrentProcess();
        environment.GetRoboPartner().LookAtTablet();

        List<Block> misalignedBlocks = wordBox.GetAllBlocks()
            .Where(block => !wordBox.HasAssignedLandingPlace(block))
            .ToList();

        //Check if all letter positions in the target word are filled
        bool allLettersFilled = !wordBox.IntervalHasUnassignedCells(0, target.collapsedWord.Length);

        if (0 == misalignedBlocks.Count)
        {
            
            scaffoldProcessRunner.SetCoroutine(AcceptanceCoroutine());

            if (allLettersFilled)
            {
                var jiboPartner = environment.GetRoboPartner();
                if (jiboPartner != null)
                {
                    jiboPartner.TriggerJumpToWordBoxEnd();
                }
                else
                {
                }

                isCompleted = true; // prevent retriggering
            }
        }
        else
        {
            correctionCount += 1;
            StartCoroutine(RejectionCoroutine(misalignedBlocks));
        }


    }

    private IEnumerator AcceptanceCoroutine()
    {
        environment.GetRoboPartner().LookAtTablet();
        List<ModPGSlotStatus> modPGSlots = ModifiedPGSlotsStates();
        Block firstNewBlock = (0 == modPGSlots.Count) ? null : FindNewBlock(modPGSlots[0]);
        AcceptSilentAlterationPGSlots(modPGSlots);
        ModPGSlotStatus[] demoPGSlots = modPGSlots.Where(pgSlot => pgSlot.status == DEMO_NEEDED).ToArray();
        List<SynQuery> synSequence = new List<SynQuery>();
        int nVowelsSkipped = 0;
        if (0 != modPGSlots.Count)
        {
            int currentPGSlotStart = IthPGSlotStart(pgSlotWeAreWorkingOn);
            int currentPGSlotEnd = currentPGSlotStart + target.pgs[pgSlotWeAreWorkingOn].GetGrapheme().Length;
            if (!ThereAreGapsBeforePGSlot(modPGSlots[0].pgPairIndex))
            {
                if (modPGSlots[0].status != DEMO_NEEDED)
                {
                    environment.GetRoboPartner().ShowExpression(RoboExpression.HAPPY);
                    synSequence.Add(RandomUtil.PickOne("scaf-accept1", ConversationMaster.ENCOURAGEMENTS));
                }
                else
                {
                    synSequence.Add(RandomUtil.PickOne("scaf-accept1", ConversationMaster.ENCOURAGEMENTS_MUTED));
                }
            }
            else
            {
                nVowelsSkipped = CheckForSkippedVowelsCase();
                if (nVowelsSkipped <= 0)
                {
                    HighlightBlock(firstNewBlock);
                    environment.GetRoboPartner().ShowExpression(RoboExpression.CURIOUS);
                    synSequence.Add(MakeEncouragementOnUnexpectedUpdate());
                    yield return synthesizer.SpeechCoroutine(SynQuery.Seq(synSequence), cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:unexpected_update", out currentSpeechID);
                    synSequence = new List<SynQuery>();
                }
                else
                {
                    synSequence.Add(RandomUtil.PickOne("scaf-accept1", ConversationMaster.ENCOURAGEMENTS_MUTED));
                }
            }
            synSequence.Add(BREAK);
        }
        if (0 != demoPGSlots.Length)
        {
            if (ShallLockKeyboardForDemo(modPGSlots, demoPGSlots))
            {
                ConstrainTouchManager();
            }
            for (int i = 0; i < demoPGSlots.Length; ++i)
            {
                if (0 < i) { synSequence.Add("Also, "); }
                yield return PerformPGSlotAlterationDisplay(demoPGSlots, i, synSequence);
                synSequence = new List<SynQuery>();
            }
            if (!touchManager.IsUnconstrained())
            {
                touchManager.Unconstrain();
            }
        }
        if (nVowelsSkipped > 0)
        {
            if (0 != demoPGSlots.Length)
            {
                synSequence.Add("Also, ");
            }
            else
            {
                synSequence.Add("Though ");
            }
            synSequence.Add($"you skipped {(1 == nVowelsSkipped ? "a vowel" : "some vowels")}.");
        }
        int nextTarget = GetFirstVacantPGSlot();
        if (nextTarget >= 0 && nextTarget != pgSlotWeAreWorkingOn) { TransitionToPGSlot(nextTarget); }
        if (0 != synSequence.Count) {
            yield return synthesizer.SpeechCoroutine(SynQuery.Seq(synSequence), cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:acceptance", out currentSpeechID);
        }
        Unhighlight();
        yield return MoveOn(nextTarget, hasPreviousRobotActions: 0 != demoPGSlots.Length);
    }

    private bool ThereAreGapsBeforePGSlot(int pgSlotI)
    {
        int pgSlotStart = 0;
        for (int i = 0; i < pgSlotI; pgSlotStart += target.pgs[i].GetGrapheme().Length, ++i)
        {
            int pgSlotEnd = pgSlotStart + target.pgs[i].GetGrapheme().Length;
            if (!wordBox.IntervalHasAssignedCells(pgSlotStart, pgSlotEnd)) return true;
        }
        return false;
    }

    private bool ShallLockKeyboardForDemo(List<ModPGSlotStatus> allModPGSlots, ModPGSlotStatus[] demoPGSlots)
    {
        if (0 == demoPGSlots.Length) return false;
        if (demoPGSlots.Length > 1) return true;
        ModPGSlotStatus demoPGSlot = demoPGSlots[0];
        if (allModPGSlots[allModPGSlots.Count - 1].pgPairIndex != demoPGSlot.pgPairIndex) return true;
        List<Block> completedBlocks = wordBox.GetBlocksAssignedToInterval(demoPGSlot.startLetter, demoPGSlot.endLetter);
        if (HasGaps(demoPGSlot.startLetter, completedBlocks)) return true;
        string completedPart = string.Join("", completedBlocks.Select(block => block.GetGrapheme()));
        if (completedPart != target.collapsedWord.Substring(demoPGSlot.startLetter, completedPart.Length)) return true;
        return false;
    }

    private bool HasGaps(int startLetter, List<Block> blocks)
    {
        foreach (Block block in blocks)
        {
            if (startLetter < wordBox.GetAssignedLandingPlace(block)) return true;
            startLetter += block.GetGrapheme().Length;
        }
        return false;
    }

    private void AcceptSilentAlterationPGSlots(List<ModPGSlotStatus> modPGSlots)
    {
        ModPGSlotStatus[] quietPGSlots = modPGSlots.Where(pgSlot => pgSlot.status == NO_CHANGE_NEEDED || pgSlot.status == SILENT_ALTERATION_NEEDED).ToArray();
        foreach (ModPGSlotStatus pgSlot in quietPGSlots)
        {
            MarkCompletion(pgSlot.startLetter, pgSlot.endLetter);
            if (pgSlot.status == SILENT_ALTERATION_NEEDED)
            {
                QuietlyAlter(pgSlot);
            }
        }
    }

    private void MarkCompletion(int startLetter, int endLetter)
    {
        for (int i = startLetter; i < endLetter; ++i)
        {
            completionMask[i] = true;
        }
    }

    private Block FindNewBlock(ModPGSlotStatus modPGSlot)
    {
        for (int i = modPGSlot.startLetter; i < modPGSlot.endLetter; ++i)
        {
            if (completionMask[i]) continue;
            Block block = wordBox.GetBlockAssignedTo(i);
            if (null != block) return block;
        }
        return null;
    }

    private SynQuery MakeEncouragementOnUnexpectedUpdate()
    {
        string[] openers = { "Aha", "I see" };
        string[] fits = { "fits", "works" };
        string[] goes_next = { "goes next", "supposed to go next" };
        string[] goes_first = { "goes first", "supposed to go first" };
        string[] goes_here = !wordBox.IntervalHasAssignedCells(0, target.pgs[0].GetGrapheme().Length) ? goes_first : goes_next;
        return $"{RandomUtil.PickOne("scaf-unexp1", openers)}! This block {RandomUtil.PickOne("scaf-unexp2", fits)}, though it is not the one that {RandomUtil.PickOne("scaf-unexp3", goes_here)}.";
    }

    private int CheckForSkippedVowelsCase()
    {
        int lastFilledPGSlot = target.pgs.Count - 1;
        for (; lastFilledPGSlot > pgSlotWeAreWorkingOn; --lastFilledPGSlot)
        {
            int pgSlotStart = IthPGSlotStart(lastFilledPGSlot);
            int pgSlotEnd = IthPGSlotStart(lastFilledPGSlot + 1);
            if (wordBox.IntervalHasAssignedCells(pgSlotStart, pgSlotEnd)) break;
        }
        if (pgSlotWeAreWorkingOn == lastFilledPGSlot) return -1; // that shouldn't happen - indicates a bug
        int vowelsSkipped = 0;
        for (int i = pgSlotWeAreWorkingOn; i < lastFilledPGSlot; ++i)
        {
            int pgSlotStart = IthPGSlotStart(i);
            int pgSlotEnd = IthPGSlotStart(i + 1);
            if (!wordBox.IntervalHasAssignedCells(pgSlotStart, pgSlotEnd))
            {
                if (PhonemeUtil.HasConsonantAspect(target.pgs[i])) return -1;
                ++vowelsSkipped;
            }
        }
        return vowelsSkipped;
    }

    private IEnumerator MoveOn(int nextTarget, bool hasPreviousRobotActions)
    {
        if (nextTarget < 0)
        {
            Unhighlight();
            List<SynQuery> synSequence = new List<SynQuery>();
            environment.GetRoboPartner().LookAtTablet();
            isCompleted = true;
            Logging.LogScaffoldingComplete(scaffolderTargetID);
            helpButton.SetActive(false);
            synSequence.Add($"And we've got ");
            synSequence.Add(wordSynQuery);
            synSequence.Add("!");
            yield return synthesizer.SpeechCoroutine(SynQuery.Seq(synSequence), cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:we-got-word", out currentSpeechID);
            environment.GetRoboPartner().ShowExpression(RoboExpression.EXCITED);
            resultBox.Refresh(targetWordSense);
            if (!touchManager.IsUnconstrained()) { touchManager.Unconstrain(); }
        }
        else
        {
            if (scaffolderDecisionModel.RobotShallTakeTurn(target.pgs[nextTarget], GetPositionType(nextTarget)))
            {
                yield return RoboTurn(nextTarget, hasPreviousRobotActions);
            }
            else
            {
                TransitionToPGSlot(nextTarget);
                yield return PromptCoroutine(nextTarget, new List<SynQuery>());
            }
        }
    }

    private int GetPositionType(int position)
    {
        for (int i = 0; i < syllableBreakdown.Length; ++i)
        {
            if (position >= syllableBreakdown[i])
            {
                position -= syllableBreakdown[i];
            }
            else
            {
                if (0 == position)
                {
                    if (0 == i)
                    {
                        return POSITION_TYPE_INITIAL;
                    }
                    else
                    {
                        return POSITION_TYPE_INITIAL_IN_SYLLABLE;
                    }
                }
                else if (syllableBreakdown[i] - 1 == position)
                {
                    if (syllableBreakdown.Length - 1 == i)
                    {
                        return POSITION_TYPE_FINAL;
                    }
                    else
                    {
                        return POSITION_TYPE_FINAL_IN_SYLLABLE;
                    }
                }
                else
                {
                    return POSITION_TYPE_MEDIAL;
                }
            }
        }
        return POSITION_TYPE_FINAL; // shouldn't really happen
    }

    private IEnumerator PerformPGSlotAlterationDisplay(ModPGSlotStatus[] modPGSlots, int slotIndex, List<SynQuery> synSequence)
    {
        ModPGSlotStatus modPGSlot = modPGSlots[slotIndex];
        HighlightPGSlot(modPGSlot.pgPairIndex);
        bool needCoreMorphing = null != modPGSlot.coreGrapheme && NeedCoreMorphing(modPGSlot);
        if (0 == slotIndex)
        {
            if (needCoreMorphing)
            {
                synSequence.Add(RandomUtil.PickOne("scaf-pg-slot-alt1", CHANGE_PHRASES));
            }
            else
            {
                synSequence.Add(RandomUtil.PickOne("scaf-pg-slot-alt1", ADD_PHRASES));
            }
            synSequence.Add(BREAK);
        }
        string pCombo = target.pgs[modPGSlot.pgPairIndex].GetPhonemeCode();
        bool morphed = false;
        bool announcement_was_made = false;
        if (needCoreMorphing) {
            Block blockAssignedToCore = wordBox.GetBlockAssignedTo(modPGSlot.coreStart);
            yield return AnnounceTheMorphedPGSlot(modPGSlot, blockAssignedToCore, synSequence);
            synSequence = new List<SynQuery>();
            yield return PerformMorphing(pCombo, modPGSlot, blockAssignedToCore);
            morphed = true;
            announcement_was_made = true;
        }
        if (!morphed && wordBox.IntervalHasUnassignedCells(modPGSlot.coreStart, modPGSlot.coreEnd))
        {
            if (!HasCorePhoneme(modPGSlot))
            {
                yield return AnnounceTheMorphedPGSlot(modPGSlot, blockAssignedToCore: null, synSequence: synSequence);
                yield return FillCore(modPGSlot);
            }
            else
            {
                yield return FillIntervalWithLetters(modPGSlot.coreStart, modPGSlot.coreEnd, modPGSlot.startLetter, modPGSlot.endLetter, synSequence, announcement_was_made, cause: "complete-core");
            }
            synSequence = new List<SynQuery>();
            announcement_was_made = true;
        }
        MarkCompletion(modPGSlot.coreStart, modPGSlot.coreEnd);
        if (!touchManager.IsUnconstrained() && modPGSlots.Skip(slotIndex + 1).All(pgSlot => !NeedCoreMorphing(pgSlot))) { touchManager.Unconstrain(); }
        yield return FillMissingLetters(modPGSlot.startLetter, modPGSlot.coreStart, modPGSlot.coreEnd, modPGSlot.endLetter, synSequence, announcement_was_made);
        Unhighlight();
        QuietlyAlter(modPGSlot);
    }

    private bool NeedCoreMorphing(ModPGSlotStatus modPGSlot)
    {
        Block coreHead = wordBox.GetBlockAssignedTo(modPGSlot.coreStart);
        return null != coreHead && HasPhoneticMatch(coreHead.GetPGPair(), modPGSlot.pgPairIndex) && coreHead.GetGrapheme() != modPGSlot.coreGrapheme;
    }

    private bool HasCorePhoneme(ModPGSlotStatus modPGSlot)
    {
        string corePhoneme = target.pgs[modPGSlot.pgPairIndex].GetUnaccentuatedPhonemeCode();
        List<Block> filledBlocks = wordBox.GetBlocksAssignedToInterval(modPGSlot.startLetter, modPGSlot.endLetter);
        return filledBlocks.Any(block => block.GetPGPair().GetUnaccentuatedPhonemeCode() == corePhoneme);
    }

    private IEnumerator AnnounceTheMorphedPGSlot(ModPGSlotStatus modPGSlot, Block blockAssignedToCore, List<SynQuery> synSequence)
    {
        PGPair pgPair = target.pgs[modPGSlot.pgPairIndex];
        string location_phrase;
        if (0 == modPGSlot.pgPairIndex) { location_phrase = $"{RandomUtil.PickOne("scaf-morph1", ScaffoldUtils.START)}s with"; }
        else if (target.pgs.Count - 1 == modPGSlot.pgPairIndex) { location_phrase = $"{RandomUtil.PickOne("scaf-morph2", ScaffoldUtils.END)}s with"; }
        else { location_phrase = "has"; }
        synSequence.Add("The word ");
        synSequence.Add(wordSynQuery);
        synSequence.Add($" {location_phrase} ");
        AddSoundAndSpellingExplanation(pgPair, graphemeUsed: blockAssignedToCore?.GetGrapheme(), synSequence);
        yield return synthesizer.SpeechCoroutine(SynQuery.Seq(synSequence),
            cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:pg-slot-morphed:{modPGSlot.pgPairIndex}",
            out currentSpeechID);
    }

    private void AddSoundAndSpellingExplanation(PGPair pgPair, string graphemeUsed, List<SynQuery> synSequence)
    {
        string phoneme = pgPair.GetUnaccentuatedPhonemeCode();
        string coreGrapheme = PhonemeUtil.GetGraphemeBreakdown(phoneme, pgPair.GetGrapheme())[1];
        SynQuery soundPhrase = SynQuery.SayAs(coreGrapheme, phoneme);
        synSequence.Add("the sound ");
        synSequence.Add(BREAK);
        synSequence.Add(soundPhrase);
        synSequence.Add(BREAK);
        synSequence.Add(", which ");
        string defaultGrapheme = PhonemeUtil.GetDefaultGrapheme(phoneme);
        if (null != graphemeUsed && graphemeUsed != coreGrapheme ||
            null == graphemeUsed && null != defaultGrapheme && coreGrapheme != defaultGrapheme)
        {
            if (null != graphemeUsed && graphemeUsed != defaultGrapheme)
            {
                if (PhonemeUtil.IsInventedSpelling(phoneme, graphemeUsed))
                {
                    synSequence.Add($"some people spell like ");
                }
                else
                {
                    synSequence.Add($"is sometimes spelled like ");
                }
                synSequence.Add(SynQuery.Spell(graphemeUsed));
            }
            else
            {
                synSequence.Add($"is usually spelled like ");
                synSequence.Add(SynQuery.Spell(defaultGrapheme));
            }
            synSequence.Add(", but in this word ");
        }
        synSequence.Add($"is spelled like ");
        synSequence.Add(SynQuery.Spell(coreGrapheme));
        synSequence.Add(".");
    }

    private IEnumerator PerformMorphing(string pCombo, ModPGSlotStatus modPGSlot, Block coreHead)
    {
        Logging.LogScaffoldingMorph(scaffolderTargetID, coreHead, wordBox.GetAssignedLandingPlace(coreHead), modPGSlot.coreGrapheme);
        MarkCompletion(modPGSlot.coreStart, modPGSlot.coreEnd);
        coreHead.Setup(new PGPair(pCombo, modPGSlot.coreGrapheme), ZSorting.GetSortingLayer(coreHead.gameObject), "scaffolder-morph");
        animaster.StartLocalGlide(coreHead.gameObject, wordBox.GetLocalPositionFor(wordBox.GetAssignedLandingPlace(coreHead) + 0.5f * coreHead.GetGrapheme().Length), BlockBase.MORPH_LENGTH);
        while (null != coreHead.GetComponent<LocalGlide>()) yield return null;
    }

    private IEnumerator FillCore(ModPGSlotStatus modPGSlot)
    {
        if (null != modPGSlot.coreGrapheme && !wordBox.IntervalHasAssignedCells(modPGSlot.coreStart, modPGSlot.coreEnd))
        {
            KeyboardKey key = FindKeyToPickGrapheme(modPGSlot.coreGrapheme);
            yield return DragBlockToLandingPlace(key, modPGSlot.coreStart, "fill-empty-core");
        }
        else
        {
            for (int i = modPGSlot.coreStart; i < modPGSlot.coreEnd; ++i)
            {
                if (null == wordBox.GetBlockAssignedTo(i))
                {
                    KeyboardKey key = FindKeyToPickGrapheme(target.collapsedWord[i].ToString());
                    yield return DragBlockToLandingPlace(key, i, "complete-partial-core");
                }
            }
        }
    }

    private IEnumerator FillMissingLetters(int startLetter, int coreStart, int coreEnd, int endLetter, List<SynQuery> synSequence, bool announcement_was_made)
    {
        yield return FillIntervalWithLetters(startLetter, coreStart, startLetter, endLetter, synSequence, announcement_was_made, "complete-slot-prefix");
        announcement_was_made |= wordBox.IntervalHasUnassignedCells(startLetter, coreStart);
        yield return FillIntervalWithLetters(coreEnd, endLetter, startLetter, endLetter, synSequence, announcement_was_made, "complete-slot-postfix");
    }

    private IEnumerator FillIntervalWithLetters(int targetStart, int targetEnd, int graphemeStart, int graphemeEnd, List<SynQuery> synSequence, bool announcement_was_made, string cause)
    {
        string[] itHas = { "it has", "it is spelled with", "there is" };
        string[] anExtra = { "an extra", "an additional" };
        string[] theSecond = { "the second", "double", "one more", "another" };
        for (int i = targetStart; i < targetEnd; ++i)
        {
            if (null == wordBox.GetBlockAssignedTo(i))
            {
                string letter = target.collapsedWord.Substring(i, 1);
                KeyboardKey key = FindKeyToPickGrapheme(letter);
                if (announcement_was_made)
                {
                    synSequence.Add(" and ");
                }
                bool doubleLetterPresent = DoubleLetterPresent(i, graphemeStart, graphemeEnd);
                if (i < target.collapsedWord.Length - 1 || doubleLetterPresent)
                {
                    string termForExtra = doubleLetterPresent ? RandomUtil.PickOne("scaf-fill-int1", theSecond) : RandomUtil.PickOne("scaf-fill-int2", anExtra);
                    synSequence.Add($" {RandomUtil.PickOne("scaf-fill-int3", itHas)} {termForExtra} letter ");
                    synSequence.Add(SynQuery.Spell(letter));
                    synSequence.Add(".");
                }
                else
                {
                    synSequence.Add(" the word ");
                    synSequence.Add(wordSynQuery);
                    synSequence.Add(" ends with letter ");
                    synSequence.Add(SynQuery.Spell(letter));
                    synSequence.Add(".");
                }
                yield return synthesizer.SpeechCoroutine(SynQuery.Seq(synSequence), cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:fill-letter:{i}", out currentSpeechID);
                synSequence = new List<SynQuery>();
                yield return DragBlockToLandingPlace(key, i, cause);
                announcement_was_made = true;
            }
        }
    }

    private bool DoubleLetterPresent(int letterI, int graphemeStart, int graphemeEnd)
    {
        return letterI - 1 >= graphemeStart && target.collapsedWord[letterI - 1] == target.collapsedWord[letterI] || letterI + 1 < graphemeEnd && target.collapsedWord[letterI + 1] == target.collapsedWord[letterI];
    }

    private KeyboardKey FindKeyToPickGrapheme(string grapheme)
    {
        GameObject[] targetObjects = (1 == grapheme.Length) ? GameObject.FindGameObjectsWithTag("KeyboardKey") : customKeyboardKeys;
        GameObject targetObject = System.Array.Find(targetObjects, gameObj => gameObj.GetComponent<KeyboardKey>().GetGrapheme() == grapheme);
        objectOfAttention = targetObject;
        return targetObject.GetComponent<KeyboardKey>();
    }

    private IEnumerator DragBlockToLandingPlace(KeyboardKey key, int landingPlace, string cause)
    {
        Logging.LogScaffoldingAutoDrag(scaffolderTargetID, phonemecode: key.GetPhonemeCode(), grapheme: key.GetGrapheme(), landingPlace: landingPlace, cause: cause);
        Block block = key.Spawn(key.transform.position);
        objectOfAttention = block.gameObject;
        block.transform.SetParent(wordBox.transform);
        MarkCompletion(landingPlace, landingPlace + block.GetGrapheme().Length);
        Debug.Log($"ASSIGNING BLOCK {block.GetGrapheme()} TO POSITION {landingPlace}");
        if (wordBox.IntervalHasAssignedCells(landingPlace, landingPlace + block.GetGrapheme().Length)) { InterruptCurrentProcess(); yield break; }
        wordBox.AssignBlockToPosition(block, landingPlace); // word box should automatically pull the block
        wordBox.QuietlyUpdateContent();
        while (null != block.GetComponent<LocalGlide>()) yield return null;
        objectOfAttention = null;
    }

    private void QuietlyAlter(ModPGSlotStatus modPGSlot)
    {
        List<Block> blocksToRemove = new List<Block>();
        List<Block> blocksToAdd = new List<Block>();
        List<Block> currentBlocks = wordBox.GetBlocksAssignedToInterval(modPGSlot.startLetter, modPGSlot.endLetter);
        foreach (Block block in currentBlocks)
        {
            int blockStart = wordBox.GetAssignedLandingPlace(block);
            int blockEnd = blockStart + block.GetGrapheme().Length;
            if (blockStart < modPGSlot.coreStart && blockEnd > modPGSlot.coreStart || blockStart < modPGSlot.coreEnd && blockEnd > modPGSlot.coreEnd)
            {
                blocksToRemove.Add(block);
                for (int i = blockStart; i < blockEnd; ++i)
                {
                    if (i < modPGSlot.coreStart || i >= modPGSlot.coreEnd)
                    {
                        blocksToAdd.Add(wordBox.SpawnBlockInBox(i, new PGPair("", target.collapsedWord[i].ToString()), startInvisible: false));
                    }
                }
            }
            else if (blockStart >= modPGSlot.coreStart && blockEnd <= modPGSlot.coreEnd && blockEnd - blockStart < modPGSlot.coreEnd - modPGSlot.coreStart)
            {
                blocksToRemove.Add(block);
            }
            else if ((blockStart < modPGSlot.coreStart || blockStart >= modPGSlot.coreEnd) && (block.GetGrapheme().Length > 1 || block.GetPhonemeCode() != "")) {
                blocksToRemove.Add(block);
                for (int i = blockStart; i < blockEnd; ++i)
                {
                    blocksToAdd.Add(wordBox.SpawnBlockInBox(i, new PGPair("", target.collapsedWord.Substring(i, 1)), startInvisible: false));
                }
            }
        }
        Block coreHeadBlock = wordBox.GetBlockAssignedTo(modPGSlot.coreStart);
        PGPair targetPG = target.pgs[modPGSlot.pgPairIndex];
        if (modPGSlot.coreStart != wordBox.GetAssignedLandingPlace(coreHeadBlock) || coreHeadBlock.GetGrapheme() != modPGSlot.coreGrapheme)
        {
            blocksToAdd.Add(wordBox.SpawnBlockInBox(modPGSlot.coreStart, new PGPair(targetPG.GetPhonemeCode(), modPGSlot.coreGrapheme), startInvisible: false));
        }
        else if (coreHeadBlock.GetPGPair().GetUnaccentuatedPhonemeCode() != targetPG.GetUnaccentuatedPhonemeCode())
        {
            coreHeadBlock.Setup(new PGPair(targetPG.GetPhonemeCode(), modPGSlot.coreGrapheme), ZSorting.GetSortingLayer(coreHeadBlock.gameObject), "scaffolder");
        }

        if (0 != blocksToAdd.Count) { wordBox.QuietlyUpdateContent(); }

        foreach (Block toRemove in blocksToRemove)
        {
            toRemove.Terminate();
        }

        if (0 != blocksToRemove.Count) { wordBox.QuietlyUpdateContent(); }
    }

    private IEnumerator RejectionCoroutine(List<Block> misalignedBlocks)
    {
        ConstrainTouchManager(); // the touch will turn back on after the prompt is given
        yield return SayRejectionMessage(misalignedBlocks);
        foreach (Block rejectedBlock in misalignedBlocks)
        {
            Logging.LogScaffoldingBlockReject(scaffolderTargetID, rejectedBlock.gameObject);
        }
        touchManager.AddAllowedToTouch(CandidateKeyboardKeys());
        wordBox.KickoutMisplacedBlocks("scaffolder");
        ElevateScaffoldingLevel();
        while (wordBox.HasKickoutBlocks()) { yield return null; }
    }

    private IEnumerator SayRejectionMessage(List<Block> misalignedBlocks)
    {
        SynQuery misBlockMessage = null;
        if (1 == misalignedBlocks.Count)
        {
            Block misBlock = misalignedBlocks[0];
            PGPair targetPG = target.pgs[GetPGSlotIndex(misBlock)];
            string targetPhCode = targetPG.GetUnaccentuatedPhonemeCode();
            string misPhCode = PhonemeUtil.Unaccentuated(misBlock.GetPhonemeCode());
            if (targetPhCode != misPhCode)
            {
                SynQuery desiredCode = SynQuery.Rate(SynQuery.SayAs(targetPG.GetGrapheme(), targetPhCode), 0.9f);
                SynQuery blockCode = SynQuery.Rate(SynQuery.SayAs(misBlock.GetGrapheme(), misPhCode), 0.9f);
                HighlightBlock(misBlock);
                misBlockMessage = SynQuery.Format("This block says {0} instead of {1}", blockCode, desiredCode);
            }
        }
        SynQuery wouldBeQuery = GenerateWouldBeQuery();
        SynQuery message = misBlockMessage;
        if (null == wouldBeQuery)
        {
            if (null != misBlockMessage)
            {
                message = misBlockMessage;
            }
            else
            {
                message = "This looks a bit off to me!";
            }
        }
        else
        {
            wouldBeQuery = SynQuery.Rate(wouldBeQuery, 0.9f);
            SynQuery desiredQuery = SynQuery.Rate(wordSynQuery, 0.9f);
            message = SynQuery.Format("this would be {0} instead of {1}.", wouldBeQuery, desiredQuery);
            if (null != misBlockMessage)
            {
                message = SynQuery.Format("{0}, so {1}", misBlockMessage, message);
            }
        }
        environment.GetRoboPartner().ShowExpression(RoboExpression.PUZZLED);
        yield return synthesizer.SpeechCoroutine(message, cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:rejection", out currentSpeechID, canInterrupt: false);
        Unhighlight();
    }

    private List<KeyboardKey> CandidateKeyboardKeys()
    {
        List<KeyboardKey> selectedKeys = new List<KeyboardKey>();
        GameObject[] keyboardKeyObjs = GameObject.FindGameObjectsWithTag("KeyboardKey");
        PGPair targetPG = target.pgs[pgSlotWeAreWorkingOn];
        int grStart = IthPGSlotStart(pgSlotWeAreWorkingOn);
        foreach (GameObject keyboardKeyObj in keyboardKeyObjs)
        {
            KeyboardKey keyboardKey = keyboardKeyObj.GetComponent<KeyboardKey>();
            if (!keyboardKey.IsActive()) continue;
            PGPair keyPGPair = keyboardKey.GetPGPair();
            int grMatchLocation = -1;
            if (HasGraphemeMatch(keyPGPair, targetPG, grStart, out grMatchLocation) || HasPhoneticMatch(keyPGPair, pgSlotWeAreWorkingOn))
            {
                selectedKeys.Add(keyboardKey);
            }
        }
        return selectedKeys;
    }

    private SynQuery GenerateWouldBeQuery() {
        List<Block>[] byPGSlot = AssortWordBoxBlocksByPGSlot();
        string wouldBeWord = AssembleWouldBeWord(byPGSlot);
        if (vocab.IsSwearWord(wouldBeWord)) return null;
        string wouldBePhonemecode = AssembleWouldBePhonemecode(byPGSlot);
        string targetPhonemecode = string.Join(";", target.pgs.Select(pg => pg.GetUnaccentuatedPhonemeCode()).Where(ph => ph != ""));
        if (CondensePhonemecode(targetPhonemecode) == CondensePhonemecode(wouldBePhonemecode)) return null;
        return SynQuery.SayAs(wouldBeWord, wouldBePhonemecode);
    }

    private string CondensePhonemecode(string phonemecode)
    {
        phonemecode = PhonemeUtil.Unaccentuated(phonemecode);
        List<string> condensed = new List<string>();
        foreach (string ph in phonemecode.Split(';'))
        {
            if (0 != condensed.Count && condensed[condensed.Count - 1] == ph) continue;
            condensed.Add(ph);
        }
        return string.Join(";", condensed);
    }

    private List<Block>[] AssortWordBoxBlocksByPGSlot()
    {
        List<Block> blocks = wordBox.GetAllBlocks();
        List<Block>[] assorter = new List<Block>[target.pgs.Count];
        for (int i = 0; i < assorter.Length; ++i)
        {
            assorter[i] = new List<Block>();
        }
        foreach (Block block in blocks)
        {
            assorter[GetPGSlotIndex(block)].Add(block);
        }
        return assorter;
    }

    private int GetPGSlotIndex(Block block)
    {
        int leftLetterI = wordBox.GetCurrentLeftLetterIndex(block);
        for (int i = 0; i < target.pgs.Count; ++i)
        {
            int graphemeLength = target.pgs[i].GetGrapheme().Length;
            if (leftLetterI < graphemeLength)
            {
                return i;
            }
            leftLetterI -= graphemeLength;
        }
        return target.pgs.Count - 1;
    }

    private string AssembleWouldBeWord(List<Block>[] assorter)
    {
        StringBuilder stringBuilder = new StringBuilder();
        for (int i = 0; i < assorter.Length; ++i) {
            List<Block> pgSlotContent = assorter[i];
            if (0 == pgSlotContent.Count)
            {
                stringBuilder.Append(target.pgs[i].GetGrapheme());
            }
            else
            {
                foreach (Block block in pgSlotContent)
                {
                    stringBuilder.Append(block.GetGrapheme());
                }
            }
        }
        return stringBuilder.ToString();
    }

    private string AssembleWouldBePhonemecode(List<Block>[] assorter)
    {
        List<string> subcodes = new List<string>();
        for (int i = 0; i < assorter.Length; ++i)
        {
            List<Block> pgSlotContent = assorter[i];
            if (0 == pgSlotContent.Count)
            {
                subcodes.Add(target.pgs[i].GetUnaccentuatedPhonemeCode());
            }
            else
            {
                foreach (Block block in pgSlotContent)
                {
                    subcodes.Add(block.GetPGPair().GetUnaccentuatedPhonemeCode());
                }
            }
        }
        return string.Join(";", subcodes.Where(subcode => 0 != subcode.Length));
    }

    private IEnumerator PromptCoroutine(int targetPos, List<SynQuery> synSequence)
    {
        Unhighlight();
        WordDrawer wordDrawer = GameObject.FindWithTag("WordDrawer").GetComponent<WordDrawer>();
        while (!wordDrawer.IsDisplayingKeyboard()) yield return null;
        Debug.Log("PROMPT COROUTINE");
        while (tutorial.IsGivingStandaloneLesson()) { yield return null; }
        IEnumerator lesson = tutorial.GetPlugInLesson("scaffolder-start", synSequence);
        if (null != lesson)
        {
            yield return lesson;
            synSequence = new List<SynQuery>();
        }
        PGPair targetPG = target.pgs[targetPos];
        if (0 == scaffoldingLevelTargeted)
        {
            scaffoldingLevelTargeted = scaffolderDecisionModel.DecideScaffoldingLevel(targetPG, GetPositionType(targetPos));
        }
        int grStart = IthPGSlotStart(targetPos);
        bool pgSlotClear = !wordBox.IntervalHasAssignedCells(grStart, grStart + targetPG.GetGrapheme().Length);
        if (pgSlotClear)
        {
            if (scaffoldingLevelEmployed > 0 && scaffoldingLevelEmployed >= scaffoldingLevelTargeted)
            {
                --scaffoldingLevelEmployed;
            }
            for (int i = scaffoldingLevelEmployed + 1; i < scaffoldingLevelTargeted + 1; ++i)
            {
                IScaffoldLevel scaffoldLevel = scaffoldLevels[i - 1];
                SynQuery levelPrompt = null;
                try
                {
                    levelPrompt = scaffoldLevel.Prompt(target, syllableBreakdown, targetPos, giveQuestion: i == scaffoldingLevelTargeted, scaffolderTargetID, scaffolderInteractionID);
                }
                catch (Exception e)
                {
                    ExceptionUtil.OnException(e);
                }
                if (null != levelPrompt)
                {
                    synSequence.Add(levelPrompt);
                    synSequence.Add(BREAK);
                }
                else if (i == scaffoldingLevelTargeted && scaffoldingLevelTargeted < scaffoldLevels.Count)
                {
                    ++scaffoldingLevelTargeted;
                }
            }
            scaffoldingLevelEmployed = scaffoldingLevelTargeted;
        }
        else
        {
            synSequence.Add(MakeGraphemePrompt(targetPos));
        }
        environment.GetRoboPartner().LookAtChild();
        lastTappedKey = null;
        yield return synthesizer.SpeechCoroutine(SynQuery.Seq(synSequence), cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:prompt", out currentSpeechID);
        environment.GetRoboPartner().LookAtTablet();
        if (!touchManager.IsUnconstrained()) { touchManager.Unconstrain(); }
        int speechID = synthesizer.Speak(RandomUtil.PickOne("scaf-kb-pick", ScaffoldUtils.KEYBOARD_QUESTIONS), cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:kb-pick");
        helpButtonFlag = false;
        while ((null == lastTappedKey || BlockBase.AnyBlockIsPlaying() || speechAccessDispatcher.IsSpeechAccessed()) && !helpButtonFlag) yield return null;
        if (synthesizer.IsSpeaking(speechID)) { synthesizer.InterruptSpeech(speechID); }
        if (helpButtonFlag)
        {
            ElevateScaffoldingLevel();
            yield return PromptCoroutine(pgSlotWeAreWorkingOn, new List<SynQuery>());
        }
        else
        {
            PGPair tappedPG = lastTappedKey.GetPGPair();
            int grMatchLocation = -1;
            if (pgSlotClear && HasPhoneticMatch(tappedPG, targetPos))
            {
                bool hasGraphemeMatch = HasGraphemeMatch(tappedPG, targetPG, grStart, out grMatchLocation);
                yield return PromptWordBoxDrag(new List<SynQuery>(), lastTappedKey.gameObject, closeMatch: hasGraphemeMatch);
            }
            else if (HasGraphemeMatch(tappedPG, targetPG, grStart, out grMatchLocation))
            {
                if (pgSlotClear)
                {
                    int grLength = tappedPG.GetGrapheme().Length;
                    string fitsOrFit = (1 == grLength) ? "fits" : "fit";
                    string letterOrLetters = (1 == grLength) ? "letter" : "letters";
                    SynQuery tappedSpelling = SynQuery.Spell(tappedPG.GetGrapheme());
                    SynQuery letterPart = SynQuery.Format($"Aha! {letterOrLetters} {{0}} {fitsOrFit}.", tappedSpelling);
                    SynQuery phonemePart = SynQuery.SayAs(targetPG.GetGrapheme(), targetPG.GetPhonemeCode());
                    SynQuery targetSpelling = SynQuery.Spell(targetPG.GetGrapheme());
                    SynQuery explanationPart = SynQuery.Format("This is because we are looking for the sound {0}, which in this word is spelled like {1}.", SynQuery.Seq(BREAK, phonemePart, BREAK), targetSpelling);
                    synSequence = new List<SynQuery>();
                    synSequence.Add(letterPart);
                    synSequence.Add(explanationPart);
                    environment.GetRoboPartner().ShowExpression(RoboExpression.HAPPY);
                    yield return PromptWordBoxDrag(synSequence, lastTappedKey.gameObject, closeMatch: true);
                }
                else
                {
                    yield return PromptWordBoxDrag(new List<SynQuery>(), lastTappedKey.gameObject, closeMatch: true);
                }
            }
            else
            {
                if (scaffoldingLevelTargeted < scaffoldLevels.Count)
                {
                    ++scaffoldingLevelTargeted;
                }
                environment.GetRoboPartner().ShowExpression(RoboExpression.PUZZLED);
                synSequence = new List<SynQuery>();
                synSequence.Add(MakeNotThisOnePrompt());
                synSequence.Add(BREAK);
                yield return PromptCoroutine(targetPos, synSequence);
            }
        }
    }

    private IEnumerator PromptWordBoxDrag(List<SynQuery> synSequence, GameObject lastTappedKey, bool closeMatch)
    {
        string[] encouragements = closeMatch ? ConversationMaster.ENCOURAGEMENTS : ConversationMaster.ENCOURAGEMENTS_MUTED;
        synSequence.Add(" " + RandomUtil.PickOne("scaf-wbox-drag1", encouragements) + " ");
        IEnumerator lesson = tutorial.GetPlugInLesson("correct-block-tapped", synSequence, extraArgument: lastTappedKey);
        if (null != lesson)
        {
            yield return lesson;
        }
        else
        {
            synSequence.Add(RandomUtil.PickOne("scaf-wbox-drag2", CAN_YOU_DRAG_PHRASES));
            yield return synthesizer.SpeechCoroutine(SynQuery.Seq(synSequence), cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:prompt-drag:{Logging.GetObjectLogID(lastTappedKey)}", out currentSpeechID);
        }
    }

    private string MakeNotThisOnePrompt()
    {
        int dice1 = RandomUtil.Range("scaf-not-this1", 0, 2);
        string openingPart = null;
        string closingPart = null;
        switch (dice1)
        {
            case 0:
                string[] nope = { "No", "Nope" };
                openingPart = $"{RandomUtil.PickOne("scaf-not-this2", nope)}, ";
                break;
            default:
                string[] good = { "Good", "Nice" };
                string[] guess = { "guess", "try" };
                openingPart = $"{RandomUtil.PickOne("scaf-not-this3", good)} {RandomUtil.PickOne("scaf-not-this4", guess)}, but ";
                break;
        }
        int dice2 = RandomUtil.Range("scaf-not-this5", 0, 3);
        string[] want = { "need", "want", "are looking for", "are searching for" };
        switch (dice2)
        {
            case 0:
                string[] notThisOne = { "not this one.", "not exactly.", "not quite." };
                closingPart = RandomUtil.PickOne("scaf-not-this6", notThisOne);
                break;
            case 1:
                string[] quite = { "exactly", "quite" };
                closingPart = $"that's not {RandomUtil.PickOne("scaf-not-this7", quite)} what we {RandomUtil.PickOne("scaf-not-this8", want)}";
                break;
            default:
                string[] aDifferentOne = { "a different one", "another one" };
                closingPart = $"we {RandomUtil.PickOne("scaf-not-this9", want)} {RandomUtil.PickOne("scaf-not-this10", aDifferentOne)}.";
                break;
        }
        return openingPart + closingPart;
    }

    private SynQuery MakeGraphemePrompt(int targetPos)
    {
        PGPair targetPG = target.pgs[targetPos];
        SynQuery targetPronunciation = SynQuery.SayAs(targetPG.GetGrapheme(), targetPG.GetPhonemeCode());
        SynQuery targetPGSpelling = SynQuery.Spell(targetPG.GetGrapheme());
        return SynQuery.Format("We are spelling {0} for the sound {1}.", targetPGSpelling, SynQuery.Seq(BREAK, targetPronunciation));
    }

    private void ElevateScaffoldingLevel()
    {
        if (scaffoldingLevelTargeted < scaffoldLevels.Count)
        {
            ++scaffoldingLevelTargeted;
        }
    }

    private IEnumerator RoboTurn(int nextPGSlot, bool hasPreviousRobotActions)
    {
        Debug.Log("ROBOTURN: starting");
        environment.GetRoboPartner().LookAtTablet();
        if (!touchManager.IsUnconstrained()) { touchManager.Unconstrain(); }
        List<SynQuery> synSequence = new List<SynQuery>();
        if (!hasPreviousRobotActions)
        {
            synSequence.Add("Let me take my turn!");
            synSequence.Add(BREAK);
        }
        for (; nextPGSlot < target.pgs.Count; ++nextPGSlot)
        {
            int startLetter = IthPGSlotStart(nextPGSlot);
            PGPair pgPair = target.pgs[nextPGSlot];
            int endLetter = startLetter + pgPair.GetGrapheme().Length;
            if (wordBox.IntervalHasAssignedCells(startLetter, endLetter)) { Debug.Log("ROBOTURN: skipping slot - filled already"); continue; }
            TransitionToPGSlot(nextPGSlot); Debug.Log($"ROBOTURN: transitioning to slot {nextPGSlot}");
            if (!scaffolderDecisionModel.RobotShallTakeTurn(target.pgs[nextPGSlot], GetPositionType(nextPGSlot))) break;
            HighlightPGSlot(nextPGSlot);
            string phonemecode = pgPair.GetUnaccentuatedPhonemeCode();
            if (nextPGSlot > 0)
            {
                if (!wordBox.IntervalHasAssignedCells(endLetter, target.collapsedWord.Length))
                {
                    synSequence.Add("Next goes ");
                }
                else
                {
                    synSequence.Add("After ");
                    synSequence.Add(SynQuery.Say(target.pgs[nextPGSlot - 1]));
                    synSequence.Add(", goes ");
                }
            }
            else
            {
                synSequence.Add(vocab.GetPronunciation(target.compositeWord));
                synSequence.Add($" begins with ");
            }
            AddSoundAndSpellingExplanation(pgPair, graphemeUsed: null, synSequence);
            string[] breakdown = PhonemeUtil.GetGraphemeBreakdown(phonemecode, pgPair.GetGrapheme());
            int coreStart = startLetter + breakdown[0].Length;
            int coreEnd = coreStart + breakdown[1].Length;
            KeyboardKey key = FindKeyToPickGrapheme(breakdown[1]);
            yield return synthesizer.SpeechCoroutine(SynQuery.Seq(synSequence), cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:robo-turn:{nextPGSlot}", out currentSpeechID);
            synSequence.Clear();
            yield return DragBlockToLandingPlace(key, coreStart, "robo-turn");
            if (0 != breakdown[0].Length + breakdown[2].Length)
            {
                yield return FillMissingLetters(startLetter, coreStart, coreEnd, endLetter, new List<SynQuery>(), true);
            }
            Unhighlight();
        }
        if (nextPGSlot < target.pgs.Count) {
            yield return synthesizer.SpeechCoroutine("And now it is your turn!", cause: $"scaffolder:{scaffolderTargetID}-{scaffolderInteractionID}:now-your-turn", out currentSpeechID);
            TransitionToPGSlot(nextPGSlot);
        }
        else
        {
            nextPGSlot = -1;
        }
        yield return MoveOn(nextPGSlot, true);
    }

    private class KeyState
    {
        public int mapping_position { get; }
        public string target_phoneme { get; }
        public bool is_aux { get; }

        public KeyState(string target_phoneme, int mapping_position, bool is_aux)
        {
            this.target_phoneme = target_phoneme;
            this.mapping_position = mapping_position;
            this.is_aux = is_aux;
        }
    }

    private bool HasPhoneticMatch(PGPair pgPair, int targetPGSlot)
    {
        string targetPCode = target.pgs[targetPGSlot].GetUnaccentuatedPhonemeCode();
        string providedPCode = pgPair.GetUnaccentuatedPhonemeCode();
        return targetPCode == providedPCode
               || PhonemeUtil.IsCommonSpelling(targetPCode, pgPair.GetGrapheme())
               || PhonemeUtil.IsInventedSpelling(targetPCode, pgPair.GetGrapheme());
    }

    private bool HasGraphemeMatch(PGPair pgPair, PGPair targetPG, int grStart, out int location)
    {
        location = -1;
        string grapheme = pgPair.GetGrapheme();
        int lastFittingPosition = grStart + targetPG.GetGrapheme().Length - grapheme.Length;
        for (int i = grStart; i <= lastFittingPosition; ++i)
        {
            if (wordBox.IntervalHasAssignedCells(i, i + grapheme.Length)) continue;
            if (target.collapsedWord.Substring(i, grapheme.Length) == grapheme) { location = i; return true; }
        }
        return false;
    }

    private int FindGraphemeLandingPosition(string grapheme, int targetPGSlot)
    {
        Debug.Log($"Finding grapheme landing position for {grapheme} in {targetPGSlot}");
        int letterZero = IthPGSlotStart(targetPGSlot);
        string targetGrapheme = target.pgs[targetPGSlot].GetGrapheme();
        for (int i = 0; i < targetGrapheme.Length - grapheme.Length + 1; ++i)
        {
            if (targetGrapheme.Substring(i, grapheme.Length) != grapheme) continue;
            if (!wordBox.IntervalHasAssignedCells(letterZero + i, letterZero + i + grapheme.Length))
            {
                return letterZero + i;
            }
            else
            {
                Debug.Log($"Interval {letterZero + i}-{letterZero + i + grapheme.Length} is occupied");
            }
        }
        return -1;
    }

    private List<ModPGSlotStatus> ModifiedPGSlotsStates()
    {
        List<ModPGSlotStatus> pgSlotStates = new List<ModPGSlotStatus>();
        int startLetter = 0;
        for (int i = 0; i < target.pgs.Count; ++i)
        {
            PGPair currentPG = target.pgs[i];
            int endLetter = startLetter + currentPG.GetGrapheme().Length;
            if (IntervalIsCompleted(startLetter, endLetter)) { startLetter = endLetter; continue; }
            List<Block> pgSlotBlocks = wordBox.GetBlocksAssignedToInterval(startLetter, endLetter);
            if (0 != pgSlotBlocks.Count)
            {
                ModPGSlotStatus status = new ModPGSlotStatus();
                status.pgPairIndex = i;
                status.startLetter = startLetter;
                status.endLetter = endLetter;
                string pCombo = currentPG.GetUnaccentuatedPhonemeCode();
                string grapheme = currentPG.GetGrapheme();
                string[] graphemeBreakdown = PhonemeUtil.GetGraphemeBreakdown(pCombo, grapheme);
                status.coreStart = startLetter + graphemeBreakdown[0].Length;
                status.coreEnd = status.coreStart + graphemeBreakdown[1].Length;
                status.coreGrapheme = graphemeBreakdown[1];
                if (pgSlotBlocks.Sum(block => block.GetGrapheme().Length) != grapheme.Length) { status.status = DEMO_NEEDED; }
                else if (string.Join("", pgSlotBlocks.Select(block => block.GetGrapheme())) != grapheme) { status.status = DEMO_NEEDED; }
                else
                {
                    Block coreBlock = wordBox.GetBlockAssignedTo(status.coreStart);
                    if (wordBox.GetAssignedLandingPlace(coreBlock) != status.coreStart
                        || coreBlock.GetGrapheme() != status.coreGrapheme
                        || coreBlock.GetPGPair().GetUnaccentuatedPhonemeCode() != pCombo)
                    {
                        status.status = SILENT_ALTERATION_NEEDED;
                    }
                    else
                    {
                        foreach (int letrI in Enumerable.Range(status.startLetter, graphemeBreakdown[0].Length)
                                                        .Concat(Enumerable.Range(status.coreEnd, graphemeBreakdown[2].Length)))
                        {
                            Block block = wordBox.GetBlockAssignedTo(letrI);
                            if (block.GetPGPair().GetPhonemeCode() != "")
                            {
                                status.status = SILENT_ALTERATION_NEEDED;
                            }
                        }
                    }
                }
                pgSlotStates.Add(status);
            }
            startLetter = endLetter;
        }
        return pgSlotStates;
    }

    private bool IntervalIsCompleted(int letterStart, int letterEnd)
    {
        for (int i = letterStart; i < letterEnd; ++i)
        {
            if (!completionMask[i]) return false;
        }
        return true;
    }

    private class KeysSchedule
    {
        private Dictionary<string, List<KeyState>> schedule_for_keys = new Dictionary<string, List<KeyState>>();

        public void AddKeyState(string key, string phoneme, int mapping_position, bool is_aux)
        {
            if (!schedule_for_keys.ContainsKey(key))
            {
                schedule_for_keys[key] = new List<KeyState>();
            }
            List<KeyState> states = schedule_for_keys[key];
            if (0 < states.Count && states[states.Count - 1].mapping_position == mapping_position) return;
            states.Add(new KeyState(phoneme, mapping_position, is_aux));
        }

        public string YieldPhoneme(string key, int mapping_position)
        {
            KeyState state = YieldState(key, mapping_position);
            if (null == state) return null;
            return state.target_phoneme;
        }

        public List<string> GetActiveMultiletterGraphemes(int mapping_position, int custom_keys_number)
        {
            List<string> multiletter_graphemes = schedule_for_keys.Keys.Where((key) => key.Length > 1).ToList();
            if (multiletter_graphemes.Count > custom_keys_number)
            {
                multiletter_graphemes.Sort((g1, g2) => CompareMultiletterGraphemes(g1, g2, mapping_position));
                multiletter_graphemes.RemoveRange(custom_keys_number, multiletter_graphemes.Count - custom_keys_number);
            }
            multiletter_graphemes.Sort((g1, g2) => g2.Length - g1.Length);
            return multiletter_graphemes;
        }

        private int CompareMultiletterGraphemes(string g1, string g2, int mapping_position)
        {
            KeyState state1 = YieldState(g1, mapping_position);
            KeyState state2 = YieldState(g2, mapping_position);
            int delta1 = StepsUntilNeeded(state1, mapping_position) - StepsUntilNeeded(state2, mapping_position);
            if (0 != delta1) return delta1;
            return (state1.is_aux ? 1 : 0) - (state2.is_aux ? 1 : 0);
        }

        private int StepsUntilNeeded(KeyState state, int mapping_position)
        {
            return (state.mapping_position >= mapping_position) ? state.mapping_position - mapping_position : 1000000;
        }

        private KeyState YieldState(string key, int mapping_position)
        {
            if (!schedule_for_keys.ContainsKey(key)) return null;
            List<KeyState> states = schedule_for_keys[key];
            if (0 == states.Count) return null;
            for (int i = 0; i < states.Count; ++i)
            {
                if (states[i].mapping_position >= mapping_position) return states[i];
            }
            return states[states.Count - 1];
        }
    }

    private const int NO_CHANGE_NEEDED = 0;
    private const int SILENT_ALTERATION_NEEDED = 1;
    private const int DEMO_NEEDED = 2;

    private void HighlightBlock(Block block)
    {
        Unhighlight();
        if (null == block) return;
        glow = Instantiate(glowPrefab);
        float unitSize = 0.9f * Block.GetStandardHeight();
        glow.transform.localScale = new Vector2(block.GetGrapheme().Length * unitSize, unitSize);
        ZSorting.SetSortingLayer(glow.gameObject, "word_drawer_ui");
        glow.transform.SetParent(block.transform, false);
        glow.transform.localPosition = new Vector3(0, 0, -1);
    }

    private void HighlightPGSlot(int slotI)
    {
        Unhighlight();
        glow = Instantiate(glowPrefab);
        float unitSize = 1.9f;
        int graphemeLength = target.pgs[slotI].GetGrapheme().Length;
        int slotStart = IthPGSlotStart(slotI);
        glow.GetComponent<SpriteRenderer>().size = new Vector2(graphemeLength * unitSize, unitSize);
        ZSorting.SetSortingLayer(glow.gameObject, "word_drawer_ui");
        glow.transform.SetParent(wordBox.transform, false);
        glow.transform.localPosition = wordBox.GetLocalPositionFor(slotStart + 0.5f * graphemeLength);
    }

    private void Unhighlight()
    {
        if (null != glow)
        {
            Destroy(glow);
            glow = null;
        }
    }

    private void ConstrainTouchManager()
    {
        touchManager.Constrain();
        touchManager.AddAllowedToTap("Block");
        touchManager.AddAllowedToTap("KeyboardKey");
    }


    private class ModPGSlotStatus
    {
        public int startLetter = 0;
        public int endLetter = 0;
        public int pgPairIndex = 0;
        public int coreStart = 0;
        public int coreEnd = 0;
        public string coreGrapheme = null;
        public int status = NO_CHANGE_NEEDED;
    }
}