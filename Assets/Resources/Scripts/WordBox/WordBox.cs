using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

public class WordBox : MonoBehaviour
{
    public static int KICKOUT_FLAG = 0;
    public static int INTERNAL_BLOCK_FLAG = 1;
    public static int TOUCHED = 4;

    private static float WORD_BOX_GLIDE_TIME = 0.5f;

    private GameObject wordDrawer = null;

    private GameObject myBg = null;
    private float bgScale = 1;
    private SpriteRenderer myBgRenderer = null;

    private List<Action> sequenceChangeCallbacks = new List<Action>();
    private List<Action> settledCallbacks = new List<Action>();
    private List<Action> updateProcessingCompletedCallbacks = new List<Action>();

    private float iniWidth = 1;

    private List<Block> myBlocks = new List<Block>();
    private List<string> utteredPhs = new List<string>();
    private List<GameObject> myCells = new List<GameObject>();

    private Dictionary<Block, HashSet<int>> blockFlags = new Dictionary<Block, HashSet<int>>();
    private Dictionary<Block, int> landingPlaces = new Dictionary<Block, int>();

    private float space;

    private GameObject blockPrefab = null;
    private GameObject cellPrefab = null;

    private AnimationMaster aniMaster = null;
    private Scaffolder scaffolder = null;

    private bool processingContentUpdate = false;
    private bool positionsSettled = true;

    private Bounds myBounds;

    void Start()
    {
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        aniMaster = stageObject.GetComponent<AnimationMaster>();
        scaffolder = stageObject.GetComponent<Scaffolder>();
        wordDrawer = GameObject.FindWithTag("WordDrawer");
        myBg = transform.Find("background").gameObject;
        myBgRenderer = myBg.GetComponent<SpriteRenderer>();
        bgScale = myBg.transform.localScale.x;
        space = 0.05f;
        iniWidth = myBgRenderer.size.x * bgScale;
        myBounds = myBgRenderer.bounds;
        blockPrefab = Resources.Load<GameObject>("Prefabs/Block");
        cellPrefab = Resources.Load<GameObject>("Prefabs/word_box_cell");
    }

    void Update()
    {
        if (scaffolder.IsComplete()) return;
        CheckForContentUpdate(notificationNeeded: true);
        ProcessTouchStatusChange();
        if (!positionsSettled) { AwaitPositionsSettled(); }
    }

    public void Clear()
    {
        foreach (Block block in myBlocks)
        {
            RaiseFlag(block, KICKOUT_FLAG);
        }
        processingContentUpdate = false;
        DirectMovements();
    }

    public void InstantClear()
    {
        foreach (Block block in myBlocks)
        {
            Logging.LogDeath(block.gameObject, "wbox-clear");
            block.Terminate();
            blockFlags.Remove(block);
        }
        blockFlags.Clear();
        landingPlaces.Clear();
        myBlocks.Clear();
        utteredPhs.Clear();
        ClearMyCells();
        foreach (Action seqChangedCallback in sequenceChangeCallbacks)
        {
            try
            {
                seqChangedCallback();
            }
            catch (Exception e)
            {
                ExceptionUtil.OnException(e);
            }
        }
        processingContentUpdate = false;
        Logging.LogWordBoxUpdate(myBlocks, "wbox-clear");
    }

    public Bounds GetMyBounds()
    {
        UpdateMyBounds();
        return myBounds;
    }

    

    public List<Block> GetBlocks()
    {
        return myBlocks.Take(AssembledPrefixLength()).ToList();
    }

    public List<Block> GetAllBlocks()
    {
        return new List<Block>(myBlocks);
    }

    public GameObject GetLastActiveCell()
    {
        if (myCells != null && myCells.Count > 0)
        {
            return myCells.Last();
        }
        return null;
    }

    public int BlocksCount()
    {
        return AssembledPrefixLength();
    }

    public void KickOut(Block block, string cause)
    {
        RaiseFlag(block, KICKOUT_FLAG);
    }

    public void KickoutMisplacedBlocks(string cause) {
        foreach (Block block in myBlocks) {
            if (!landingPlaces.ContainsKey(block)) {
                RaiseFlag(block, KICKOUT_FLAG);
            }
        }
        DirectMovements();
    }

    public bool HasKickoutBlocks()
    {
        return myBlocks.Any(block => HasFlag(block, KICKOUT_FLAG));
    }

    public void AddSequenceChangedCallback(Action callback)
    {
        sequenceChangeCallbacks.Add(callback);
    }

    public void AddSettledCallback(Action callback) {
        settledCallbacks.Add(callback);
    }

    public void AddUpdateProcessingCompletedCallback(Action callback) {
        updateProcessingCompletedCallbacks.Add(callback);
    }

    public void ActivateScaffolding()
    {
        int wordLength = scaffolder.GetTarget().collapsedWord.Length;
        float virtualWidth = ComputeVirtualWidth(wordLength);
        ChangeCapacity(virtualWidth, 0.5f);
        SpawnMyCells();
    }

    public bool ManagingBlock(Block block)
    {
        return !HasFlag(block, KICKOUT_FLAG) && IsMyBlock(block);
    }

    public float GetLocalRightEdge()
    {
        return space + Block.GetStandardHeight() * myBlocks.Take(AssembledPrefixLength()).Select(block => block.GetGrapheme().Length).Sum();
    }

    public Vector2 GetPositionFor(float letterIndex)
    {
        return new Vector2(transform.position.x + transform.localScale.x * (space + Block.GetStandardHeight() * letterIndex), transform.position.y);
    }

    public Vector2 GetLocalPositionFor(float letterIndex)
    {
        return new Vector2(space + Block.GetStandardHeight() * letterIndex, 0);
    }

    public float GetScale()
    {
        return transform.localScale.x;
    }

    private void UpdateMyBounds()
    {
        Bounds currentBounds = myBgRenderer.bounds;
        myBounds.center = currentBounds.center;
        myBounds.extents.Set(currentBounds.extents.x, myBounds.extents.y, myBounds.extents.z);
    }

    public bool HasAssignedLandingPlace(Block block) {
        return landingPlaces.ContainsKey(block);
    }

    public bool IntervalHasUnassignedCells(int letterMin, int letterMax)
    {
        if (letterMax <= letterMin) return false;
        foreach (Block block in myBlocks)
        {
            int position;
            if (landingPlaces.TryGetValue(block, out position))
            {
                if (letterMin < position) return true;
                int rightEnd = position + block.GetGrapheme().Length;
                if (letterMin < rightEnd) { letterMin = rightEnd; }
                if (letterMax <= letterMin) return false;
            }
        }
        return true;
    }

    public bool IntervalHasAssignedCells(int letterMin, int letterMax)
    {
        foreach (Block block in myBlocks)
        {
            int position;
            if (landingPlaces.TryGetValue(block, out position))
            {
                if (BoundsUtil.IntervalsOverlap(position, position + block.GetGrapheme().Length, letterMin, letterMax))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public Block GetBlockAssignedTo(int letterI)
    {
        foreach (Block block in myBlocks)
        {
            int position;
            if (landingPlaces.TryGetValue(block, out position))
            {
                if (position <= letterI && letterI < position + block.GetGrapheme().Length)
                {
                    return block;
                }
            }
        }
        return null;
    }

    // TODO: semantics of this function might need revision
    public List<Block> GetBlocksAssignedToInterval(int letterMin, int letterMax)
    {
        List<Block> selectedBlocks = new List<Block>();
        foreach (Block block in myBlocks)
        {
            int position;
            if (landingPlaces.TryGetValue(block, out position))
            {
                if(letterMin <= position && position < letterMax) selectedBlocks.Add(block);
            }
        }
        return selectedBlocks;
    }

    public int GetCurrentLeftLetterIndex(Block block)
    {
        return (int)((block.transform.localPosition.x - space) / Block.GetStandardHeight());
    }

    public int GetAssignedLandingPlace(Block block)
    {
        if (!landingPlaces.ContainsKey(block)) return -1;
        return landingPlaces[block];
    }

    public Block SpawnBlockInBox(int letterPosition, PGPair pgPair, bool startInvisible)
    {
        Block block = CreateBlockFor(pgPair, startInvisible);
        block.transform.localPosition = new Vector3(space + Block.GetStandardHeight() * (letterPosition + 0.5f * pgPair.GetGrapheme().Length), 0, -0.1f);
        AssignBlockToPosition(block, letterPosition);
        return block;
    }

    public void AssignBlockToPosition(Block block, int firstLetter)
    {
        landingPlaces[block] = firstLetter;
        block.GetComponent<Draggable>().LockDrag();
        // CheckForContentUpdate(notificationNeeded: false);
    }

    public void QuietlyUpdateContent()
    {
        CheckForContentUpdate(notificationNeeded: false);
    }

    // THE GREAT UPDATE SEQUENCE START

    // STEP 0. Identifying blocks on Word Box

    private List<Block> FindMyBlocks()
    {
        return GameObject.FindGameObjectsWithTag("Block")
                         .Where(blockObj => "word_drawer" == ZSorting.GetSortingLayer(blockObj))
                         .Select(blockObject => blockObject.GetComponent<Block>())
                         .Where(block => IsMyBlock(block))
                         .OrderBy(block => BlockAbsoluteX(block))
                         .ToList();
    }

    private bool IsMyBlock(Block block)
    {
        if (!block.IsAlive()) return false;
        if (scaffolder.IsComplete()) { return myBlocks.Contains(block); }
        if (landingPlaces.ContainsKey(block)) return true;
        UpdateMyBounds();
        return BoundsUtil.Contains2D(myBounds, block.transform.position) || block.IsTouched() && BoundsUtil.Contains2D(myBounds, block.CurrentTouchPos());
    }

    private float BlockAbsoluteX(Block block)
    {
        if (!landingPlaces.ContainsKey(block))
        {
            return block.transform.position.x;
        }
        else
        {
            return transform.position.x + transform.localScale.x * (space + Block.GetStandardHeight() * (landingPlaces[block] + 0.5f * block.GetGrapheme().Length));
        }
    }

    // STEP 1. Handling content update

    private void CheckForContentUpdate(bool notificationNeeded)
    {
        List<Block> newBlocks = FindMyBlocks();
        if (!myBlocks.SequenceEqual(newBlocks)) { OnContentUpdate(newBlocks, notificationNeeded); }
    }

    private void OnContentUpdate(List<Block> newBlocks, bool notificationNeeded)
    {
        Debug.Log("UPDATE SEQUENCE: CONTENT UPDATE " + string.Join(";", newBlocks.Select(block => block.GetGrapheme())));
        processingContentUpdate = notificationNeeded;
        Debug.Log("PROCESSING CONTENT UPDATE TRUE");
        Block[] arrivedBlocks = GetArrivedBlocks(newBlocks);
        Block[] departedBlocks = GetDepartedBlocks(newBlocks);
        AttachArrivedBlocks(arrivedBlocks);
        DetachDepartedBlocks(departedBlocks);
        myBlocks = newBlocks;
        Logging.LogWordBoxUpdate(myBlocks, "auto");
        foreach (Action callback in sequenceChangeCallbacks)
        {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                ExceptionUtil.OnException(e);
            }
        }
        DirectMovements();
    }

    private Block[] GetArrivedBlocks(List<Block> newBlocks)
    {
        return newBlocks.Where(newBlock => !myBlocks.Contains(newBlock)).ToArray();
    }

    private Block[] GetDepartedBlocks(List<Block> newBlocks)
    {
        return myBlocks.Where(oldBlock => null != oldBlock && !newBlocks.Contains(oldBlock)).ToArray();
    }

    private void AttachArrivedBlocks(Block[] arrivedBlocks)
    {
        foreach (Block newBlock in arrivedBlocks)
        {
            newBlock.transform.SetParent(transform, true);
            Vector3 localPos = newBlock.transform.localPosition;
            newBlock.transform.localPosition = new Vector3(localPos.x, localPos.y, -1);
            Logging.LogParent(newBlock.gameObject, "wbox_join");
            float blockScale = newBlock.IsTouched() ? TouchManager.INFLATION_ON_TAP : 1;
            newBlock.transform.localScale = new Vector3(blockScale, blockScale, 1);
            Logging.LogScale(newBlock.gameObject, "wbox_join");
            if (newBlock.IsTouched())
            {
                RaiseFlag(newBlock, TOUCHED);
            }
            else
            {
                OnRelease(newBlock);
            }
        }
    }

    private void DetachDepartedBlocks(Block[] departedBlocks)
    {
        foreach (Block oldBlock in departedBlocks)
        {
            if (oldBlock.transform.parent == transform)
            {
                oldBlock.transform.SetParent(wordDrawer.transform, true);
                Logging.LogParent(oldBlock.gameObject, "wbox_release");
                oldBlock.transform.localScale = new Vector3(1, 1, 1);
                Logging.LogScale(oldBlock.gameObject, "wbox_release");
                blockFlags.Remove(oldBlock);
                landingPlaces.Remove(oldBlock);
            }
        }
    }

    // STEP 2. Awaiting touch release.

    private void ProcessTouchStatusChange()
    {
        bool anyWasTouched = myBlocks.Any(block => HasFlag(block, TOUCHED));
        bool anyReleased = false;
        foreach (Block block in myBlocks)
        {
            if (!block.IsTouched())
            {
                if (HasFlag(block, TOUCHED))
                {
                    OnRelease(block);
                    anyReleased = true;
                    EraseFlag(block, TOUCHED);
                }
            }
            else
            {
                RaiseFlag(block, TOUCHED);
            }
        }
        if (anyReleased) {
            Debug.Log("UPDATE SEQUENCE: ON RELEASE " + string.Join(";", myBlocks.Select(block => block.GetGrapheme())));
            DirectMovements();
        }
    }

    private void OnRelease(Block block)
    {
        FindLandingPlace(block);
    }

    private void FindLandingPlace(Block block)
    {
        if (landingPlaces.ContainsKey(block)) return; // the block has pre-assigned landing place
        int vacantPGSlot = scaffolder.GetFirstVacantPGSlot();
        if (vacantPGSlot < 0) return;
        int maxPGSlot = vacantPGSlot;
        Debug.Log("PROBING VACANT SLOT");
        int landingPosition = scaffolder.GetLandingPosition(block.GetPGPair(), vacantPGSlot);
        List<PGPair> target = scaffolder.GetTarget().pgs;
        if (landingPosition < 0)
        {
            int vacantSlotStart = scaffolder.IthPGSlotStart(vacantPGSlot);
            int vacantSlotEnd = scaffolder.IthPGSlotStart(vacantPGSlot + 1);
            if (!PhonemeUtil.HasConsonantAspect(target[vacantPGSlot]) || IntervalHasAssignedCells(vacantSlotStart, vacantSlotEnd))
            {
                for (maxPGSlot = vacantPGSlot + 1; maxPGSlot < target.Count; ++maxPGSlot)
                {
                    Debug.Log($"PROBING CANDIDATE SLOT v+{maxPGSlot - vacantPGSlot}");
                    landingPosition = scaffolder.GetLandingPosition(block.GetPGPair(), maxPGSlot);
                    if (landingPosition >= 0) break;
                    if (PhonemeUtil.HasConsonantAspect(target[maxPGSlot]))
                    {
                        int maxPGSlotStart = scaffolder.IthPGSlotStart(maxPGSlot);
                        int maxPGSlotEnd = maxPGSlotStart + target[maxPGSlot].GetGrapheme().Length;
                        if (!IntervalHasAssignedCells(maxPGSlotStart, maxPGSlotEnd)) break;
                    }
                }
            }
        }
        if (landingPosition < 0)
        {
            int currentPGSlot = scaffolder.GetPGSlot((block.transform.localPosition.x - space) / Block.GetStandardHeight());
            if (currentPGSlot <= maxPGSlot)
            {
                Debug.Log("NO LANDING FOUND");
                return;
            }
            Debug.Log("PROBING SLOT BELOW");
            landingPosition = scaffolder.GetLandingPosition(block.GetPGPair(), currentPGSlot);
        }
        if (landingPosition < 0)
        {
            Debug.Log("NO LANDING FOUND");
            return;
        }
        Debug.Log($"FOUND LANDING {landingPosition}");
        Logging.LogScaffoldingBlockAccept(scaffolder.GetTargetID(), block.gameObject, landingPosition);
        landingPlaces[block] = landingPosition;
        block.GetComponent<Draggable>().LockDrag();
    }

    // STEP 3. Moving blocks to their places.

    private void DirectMovements()
    {
        Debug.Log("UPDATE SEQUENCE: DIRECT MOVEMENTS");
        List<float> xTargets = GetXTargetsAndLabelCrammedBlocks();
        for (int i = 0; i < myBlocks.Count(); ++i)
        {
            Block block = myBlocks[i];
            if (block.IsTouched()) continue;
            if (HasFlag(block, KICKOUT_FLAG)) continue;
            float yTarget = 0;
            float xTarget = xTargets[i];
            LocalGlide glide = block.GetComponent<LocalGlide>();
            if (null != glide
                && Mathf.Abs(glide.GetEnd().x - xTarget) < 0.01
                && Mathf.Abs(glide.GetEnd().y - yTarget) < 0.01) continue;
            Vector2 localPosition = block.transform.localPosition;
            if (Mathf.Abs(localPosition.x - xTarget) < 0.01 && Mathf.Abs(localPosition.y - yTarget) < 0.01)
            {
                if (null != glide) glide.Stop();
                RaiseFlag(block, INTERNAL_BLOCK_FLAG);
                continue;
            }
            aniMaster.StartLocalGlide(block.gameObject, new Vector2(xTarget, yTarget), WORD_BOX_GLIDE_TIME);
            positionsSettled = false;
        }
        AwaitPositionsSettled();
    }

    private List<float> GetXTargetsAndLabelCrammedBlocks()
    {
        float offset = space;
        List<float> targets = new List<float>();
        for (int i = 0; i < myBlocks.Count(); ++i)
        {
            Block block = myBlocks[i];
            if (HasFlag(block, KICKOUT_FLAG))
            {
                targets.Add(block.transform.localPosition.x);
            }
            else
            {
                float level = 0;
                if (landingPlaces.ContainsKey(block))
                {
                    level = space + Block.GetStandardHeight() * landingPlaces[block] + 0.5f * block.GetWidth();
                }
                else
                {
                    level = offset + 0.5f * block.GetWidth();
                }
                offset = Mathf.Max(offset, level + 0.5f * block.GetWidth());
                if (block.IsTouched())
                {
                    targets.Add(block.transform.localPosition.x);
                }
                else
                {
                    targets.Add(level);
                }
            }
        }
        return targets;
    }

    private void AwaitPositionsSettled() {
        //Debug.Log("UPDATE SEQUENCE: AWAIT POS SETTLED...");
        if (myBlocks.Any(block => block.IsTouched())) return;
        if (myBlocks.Any(block => null != block.GetComponent<Glide>())) return;
        if (myBlocks.Any(block => null != block.GetComponent<LocalGlide>())) return;
        if (myBlocks.Any(block => HasFlag(block, KICKOUT_FLAG))) return;
        Debug.Log("UPDATE SEQUENCE: POS SETTLED! " + string.Join(";", myBlocks.Select(block => block.GetGrapheme())) + " Processing content update: " + processingContentUpdate);
        positionsSettled = true;
        if (processingContentUpdate) {
            foreach (Action settledCallback in settledCallbacks) {
                try
                {
                    settledCallback();
                }
                catch (Exception e)
                {
                    ExceptionUtil.OnException(e);
                }
            }
            OnUpdateProcessingComplete();
        }
    }

    // STEP 4. Sounding out.

    // Removed as obsolete

    // STEP 5. Finishing update processing.

    private void OnUpdateProcessingComplete() {
        Debug.Log("UPDATE SEQUENCE: ON UPDATE PROCESSING COMPLETE");
        processingContentUpdate = false;
        Debug.Log("PROCESSING CONTENT UPDATE FALSE");
        foreach (Action callback in updateProcessingCompletedCallbacks) {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                ExceptionUtil.OnException(e);
            }
        }
    }

// THE GREAT UPDATE SEQUENCE END

    private void RaiseFlag(Block block, int flag)
    {
        if (!blockFlags.ContainsKey(block)) { blockFlags[block] = new HashSet<int>(); }
        blockFlags[block].Add(flag);
    }

    private void EraseFlag(Block block, int flag)
    {
        if (!blockFlags.ContainsKey(block)) return;
        blockFlags[block].Remove(flag);
    }

    public bool HasFlag(Block block, int flag)
    {
        if (!blockFlags.ContainsKey(block)) return false;
        return blockFlags[block].Contains(flag);
    }

// TRANSITIONS START
    private Block CreateBlockFor(PGPair pgPair, bool startInvisible)
    {
        GameObject blockObject = (GameObject)Instantiate(blockPrefab);
        Block block = blockObject.GetComponent<Block>();
        block.Setup(pgPair.Unaccentuated(), "word_drawer", "auto");
        ConfigRefBlock(block, startInvisible);
        return block;
    }

    private void ConfigRefBlock(Block block, bool startInvisible)
    {
        block.transform.SetParent(transform, false);
        if (startInvisible) { Opacity.SetOpacity(block.gameObject, 0); }
        RaiseFlag(block, INTERNAL_BLOCK_FLAG);
    }

    private float ComputeVirtualWidth(int letterCount)
    {
        return Block.GetStandardHeight() * (letterCount + 2 * space);
    }

    private void ChangeCapacity(float virtualWidth, float duration)
    {
        aniMaster.StartResize(myBg, virtualWidth / bgScale, 1.1f, duration);
        aniMaster.StartLocalGlide(myBg, new Vector2(virtualWidth / 2, 0), duration);
        float targetScale = Mathf.Min(iniWidth / virtualWidth, 1);
        aniMaster.StartScale(gameObject, new Vector3(targetScale, targetScale, 1), duration);
    }

// TRANSITIONS END

// SCAFFOLDING PROPS START
    private void SpawnMyCells() {
        float leftCorner = space;
        int letterCount = scaffolder.GetTarget().collapsedWord.Length;
        for (int i = 0; i < letterCount; ++i) {
            float expectedX = leftCorner + 0.5f * Block.GetStandardHeight();
            leftCorner += Block.GetStandardHeight();
            GameObject cell = Instantiate(cellPrefab);
            cell.transform.SetParent(transform, false);
            cell.transform.localPosition = new Vector3(expectedX, 0, -1);
            cell.GetComponent<SpriteRenderer>().size = new Vector2(1, 1);
            myCells.Add(cell);
        }
    }

    private void ClearMyCells() {
        foreach (GameObject cell in myCells) {
            if (null != cell) { Destroy(cell); }
        }
        myCells.Clear();
    }

// SCAFFOLDING PROPS END

    private int AssembledPrefixLength()
    {
        int currentLocation = 0;
        for (int i = 0; i < myBlocks.Count; ++i)
        {
            Block block = myBlocks[i];
            if (!landingPlaces.ContainsKey(block)) return i;
            if (currentLocation < landingPlaces[block]) return i;
            currentLocation += block.GetGrapheme().Length;
        }
        return myBlocks.Count;
    }

  
}

