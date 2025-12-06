using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class BlockManager : MonoBehaviour {
    private WordBox wordBox = null;

    private void Start()
    {
        wordBox = GameObject.FindWithTag("WordBox").GetComponent<WordBox>();
    }

    private void Update()
    {
        IEnumerable<GameObject> drawerBlocks = GameObject.FindGameObjectsWithTag("Block").Where(block => "word_drawer" == ZSorting.GetSortingLayer(block));
        foreach (GameObject drawerBlockObject in drawerBlocks) {
            Block managedBlock = drawerBlockObject.GetComponent<Block>();
            if (wordBox.ManagingBlock(managedBlock)) continue;
            if (managedBlock.IsTouched()) continue;
            Debug.Log("Managing block");
            if (0 != managedBlock.GetComponents<IAnimation>().Length) continue;
            GameObject toFallOn = KeyboardKeyToFallOn(managedBlock);
            if (null == toFallOn) { 
                Logging.LogDeath(managedBlock.gameObject, "blk-mgr");
                managedBlock.Terminate(); 
            } else {
                Chase(managedBlock, toFallOn);
            }
        }
    }

    private void Chase(Block block, GameObject keyboardKey) {
        Vector3 blockPos = block.transform.position;
        Vector3 delta = keyboardKey.transform.position - blockPos;
        float deltaMag = delta.magnitude;
        float movT = 10 * Time.deltaTime;
        float newMag = deltaMag - movT;
        if (newMag < 0) {
            Logging.LogDeath(block.gameObject, "blk-mgr");
            block.Terminate();
        } else {
            Bounds wordBoxBounds = wordBox.GetMyBounds();
            if (blockPos.y < wordBoxBounds.max.y) { 
                block.transform.position = new Vector3(blockPos.x, keyboardKey.transform.position.y - delta.y * (newMag / deltaMag), blockPos.z);
            } else {
                block.transform.position = keyboardKey.transform.position - delta * (newMag / deltaMag);
            }
            Logging.LogMovement(block.gameObject, "blk-mgr");
        }
    }

    private GameObject KeyboardKeyToFallOn(Block block)
    {
        return GameObject.FindGameObjectsWithTag("KeyboardKey")
                            .Where(obj => obj.GetComponent<KeyboardKey>().GetGrapheme() == block.GetGrapheme())
                            .FirstOrDefault();
    }
}
