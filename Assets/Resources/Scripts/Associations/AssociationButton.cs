using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AssociationButton : MonoBehaviour, IDetailedLogging, ITappable
{
    private string word_sense;
    private string reason;
    private SynthesizerController synthesizer;
    private int position;

    public void Setup(WordSuggestion suggestion, int position)
    {
        this.position = position;
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        synthesizer = stageObject.GetComponent<SynthesizerController>();
        this.word_sense = suggestion.GetWordSense();
        this.reason = suggestion.GetReason();
        float picSize = 1.5f * GetComponent<CircleCollider2D>().radius;
        GetComponent<Picture>().Setup(word_sense, picSize, picSize, "stage_ui");
    }

    public string GetWordSense()
    {
        return word_sense;
    }

    public float GetButtonSize()
    {
        GameObject bg = transform.Find("bg").gameObject;
        SpriteRenderer bgRenderer = bg.GetComponent<SpriteRenderer>();
        return bgRenderer.sprite.rect.size.x * bg.transform.localScale.x / bgRenderer.sprite.pixelsPerUnit;
    }

    public int GetPosition()
    {
        return position;
    }

    public object[] GetLogDetails()
    {
        return new object[] { "assoc-word", word_sense };
    }

    public void OnTap(TouchInfo touchInfo) {
        //GameObject[] associationButtons = GameObject.FindGameObjectsWithTag("AssociationButton");
        if (!transform.parent.GetComponent<AssociationsPanel>().Select(this)) return;
        Vocab vocab = GameObject.FindWithTag("StageObject").GetComponent<Vocab>();
        List<SynQuery> sequence = new List<SynQuery>();
        sequence.Add(vocab.GetPronunciation(Vocab.GetWord(word_sense)));
        if (null != reason)
        {
            sequence.Add(SynQuery.Break(0.3f));
            sequence.Add(reason);
        }
        synthesizer.Speak(SynQuery.Seq(sequence), cause: "assoc-button-tap", keepPauses: false);
    }

    public WordSuggestion GetWordSuggestion()
    {
        return new WordSuggestion(word_sense, reason);
    }
}
