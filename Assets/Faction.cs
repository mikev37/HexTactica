using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName = "Faction")]
public class Faction : ScriptableObject {
    public Sprite icon;
    public string description;
    public List<Unit> unitlist;
}
