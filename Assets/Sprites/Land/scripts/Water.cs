using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Water : MonoBehaviour
{
	Vector3 pos;
	float rando;
	float rando2;
    // Start is called before the first frame update
    void Start()
    {
		pos = transform.position;
		rando =  Random.value;
		rando2 =  Random.value;
    }

    // Update is called once per frame
    void Update()
    {
		transform.localScale = new Vector3(Mathf.PingPong(Time.time + rando2 * 2, 2) * .5f * .2f + 1, Mathf.PingPong(Time.time + rando * 2, 2) * .5f * .2f + 1f);
		transform.position = pos -  new Vector3(0, Mathf.PingPong(Time.time + rando, 2)/2*.2f) - Vector3.up *.4f;
	}
}
