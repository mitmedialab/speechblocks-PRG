using System.Collections.Generic;
using UnityEngine;

public enum RoboExpression
{
    GREETING,
    HAPPY,
    EMBARRASSED,
    CURIOUS,
    PUZZLED,
    EXCITED
}

public interface IRoboPartner
{
    bool HasEntered();
    void LookAtChild();
    void LookAtTablet();
    void ShowExpression(RoboExpression expression);
    void AtEase();
    void SuggestObjectsOfInterest(List<GameObject> objectsOfInterest);
    void TriggerJumpToWordBoxEnd();
}
