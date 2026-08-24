using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName = "MoveType")]
public class MovementData : ScriptableObject {

	[TextArea(3, 10)]
	public string description;

	[Header("UI Variables")]
	public Sprite display;
	[Range(0, 10)]
	public int moveRange;
	[Range(0, 2)]
	public float terrainImpact;
	[Range(0, 10)]
	public int minFuelPerTurn;
	[Range(0, 3)]
	public int minFuelPerTile;
	[Range(-2, 3)]
	public int elevationMin;
	[Range(-2, 3)]
	public int elevationMax;

}
