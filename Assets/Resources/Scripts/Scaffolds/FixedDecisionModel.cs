using System.Linq;

public class FixedDecisionModel : IScaffolderDecisionModel
{
    public bool RobotShallTakeTurn(PGPair pgPair, int positionType)
    {
        return positionType == Scaffolder.POSITION_TYPE_MEDIAL || pgPair.GetPhonemes().Any(ph => PhonemeUtil.IsVowelPhoneme(ph));
    }

    public int DecideScaffoldingLevel(PGPair pgPair, int positionType)
    {
        return 1;
        // return 3;
    }
}