using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace OrbitalCombat{
	[RequireComponent (typeof (SpriteRenderer))]
	public class SpriteAnimator : MonoBehaviour
	{
		public bool cleanUp;
		public Sprite[] frames;
		public SpriteRenderer display;
		public int frame;
		public float timer;
		public float speed;
	    // Start is called before the first frame update
	    void Start()
	    {
			display = GetComponent<SpriteRenderer> ();
			timer = speed;
	    }

	    // Update is called once per frame
	    void Update()
	    {
			timer -= Time.deltaTime;
			if (timer <= 0) {
				timer = speed;
				frame++;
				if (frame >= frames.Length) {
					frame = 0;
					if (cleanUp) {
						Destroy (this.gameObject);
					}
				}
				display.sprite = frames [frame];
			}
	    }
	}
}