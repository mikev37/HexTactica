using System.Collections;
using System.Collections.Generic;
using UnityEngine;

	[CreateAssetMenu(menuName = "Terrain")]
	public class TerrainData : ScriptableObject {

		[TextArea(3, 10)]
		public string description;

		[Header("Terrain Sprites")]
		public Sprite upper_normal;
		public Sprite upper_damaged;
		public Sprite upper_destroyed;

		public Sprite lower;
		public Sprite floor;
		public Sprite water;

		[Header("Terrain Variables")]
		[Range(-1,3)]
		[Tooltip("how much defense this tile provides")]
		public int cover;
		[Range(0, 5)]
		[Tooltip("how much to subtract to move through this land")]
		public int moveability;
		[Tooltip("negative for water, 0 for bridge, one for land, 2 hills, 3 mountains")]
		[Range(-2, 3)]
		public int elevation;
		[Tooltip("draw roads")]
		public bool road;
	}

/*
public enum ShipClass {
	DRONE,
	CORVETTE,
	DESTROYER,
	FRIGATE,
	CRUISER,
	CAPITAL,
	BATTLECRUISER
}

public enum Faction {
	PIRATE,
	FEDERATION,
	UNAFFILIATED,
	GUANTI,
	YORSHAU,
	TERMINUS,
	GALAXARIAN,
	ARTFICER,
	INFECTED
}

 */