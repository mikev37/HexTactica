using System.Collections;
using System.Collections.Generic;
using UnityEngine;

	[CreateAssetMenu(menuName = "Terrain")]
	public class TerrainData : ScriptableObject {

		[TextArea(3, 10)]
		public string description;

		[Header("Terrain Sprites")]
		public Sprite upper_normal;
		[Tooltip("Older single damage picture: used as the first damage state when Damage States is empty.")]
		public Sprite upper_damaged;
		[Tooltip("Older second damage picture: used as the second damage state when Damage States is empty.")]
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

		[Header("Destruction")]
		[Tooltip("Hits it takes before it is destroyed: every attack on a unit standing on it is one. 0: never damaged.")]
		public int health;
		[Tooltip("Its top sprite as it wears down, in order. The health is split into this many + 1 equal parts: the first part (full health) shows the normal sprite, each later part the next picture here.")]
		public Sprite[] damageStates;
		[Tooltip("What it turns into at 0 health, as if painted in the level editor (Forest: Stumps; Bridge: Water). Empty: it stays, in its last damage picture.")]
		public TerrainData destroyedInto;

		/// <summary>The damage pictures in order (Damage States, else the older upper_damaged / upper_destroyed).</summary>
		public Sprite[] DamageStates {
			get {
				if (damageStates != null && damageStates.Length > 0) return damageStates;
				if (upper_damaged == null && upper_destroyed == null) return new Sprite[0];
				if (upper_destroyed == null) return new[] { upper_damaged };
				if (upper_damaged == null) return new[] { upper_destroyed };
				return new[] { upper_damaged, upper_destroyed };
			}
		}

		/// <summary>
		/// Which picture shows with <paramref name="damage"/> taken out of <paramref name="health"/>,
		/// with <paramref name="states"/> damage pictures: 0 the normal one, k the k-th damage picture.
		/// The health is split into states + 1 equal parts, from full: health 10 and one picture, 10-6
		/// normal and 5-1 damaged; two pictures, 10-7, 6-4 and 3-1.
		/// </summary>
		public static int Stage(int health, int damage, int states) {
			if (health <= 0 || states <= 0 || damage <= 0) return 0;
			int left = Mathf.Clamp(health - damage, 0, health);
			if (left <= 0) return states;
			int parts = states + 1;
			int part = Mathf.CeilToInt(left * parts / (float)health - 1e-4f);   // parts..1, full health in the highest
			return Mathf.Clamp(parts - part, 0, states);
		}
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