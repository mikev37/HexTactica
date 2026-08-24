using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class counterRotate : MonoBehaviour
{
    // Update is called once per frame
    void Update()
    {
        if(transform.rotation != Quaternion.identity) {
            transform.rotation = Quaternion.identity;
        }
    }
}
