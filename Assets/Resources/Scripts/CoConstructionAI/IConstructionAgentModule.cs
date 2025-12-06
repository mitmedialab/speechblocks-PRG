using System;
using System.Collections;
using System.Collections.Generic;

public interface IConstructionAgentModule
{
    void UpdateLastestScene();
    void UpdateChildUtterance();
    IEnumerator GetRobotIntegrativeElaboration(byte[] sceneSnapshot, List<string> onsetItems, string new_object);
    IEnumerator GetRobotSymbolicPlayBehavior(byte[] sceneSnapshot, List<string> onsetItems, string new_object);
    void ClearAgentTurnStatus();
}
