using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName = "Weapon")]
public class WeaponData: ScriptableObject {

	[TextArea(3, 10)]
	public string description;

	[Header("UI Variables")]
	public Sprite display;

	[Header("Terrain Variables")]
	[Range(-1, 3)]
	[Tooltip("how much defense this tile provides")]
	public int cover;
	[Range(1, 3)]
	[Tooltip("how much to subtract to move through this land")]
	public int moveability;
	[Tooltip("negative for water, 0 for bridge, one for land, 2 hills, 3 mountains")]
	[Range(-2, 3)]
	public int elevation;
	[Tooltip("draw roads")]
	public bool road;

}
