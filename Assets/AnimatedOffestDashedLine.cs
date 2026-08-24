using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimatedOffestDashedLine : MonoBehaviour
{
    Material mr;
    LineRenderer lr;
    public float offset;
    public float speed;
    // Start is called before the first frame update
    void Start()
    {
        lr = GetComponent<LineRenderer>();
        mr = lr.material;
    }

    // Update is called once per frame
    void Update()
    {
        offset += Time.deltaTime * speed;
        mr.mainTextureOffset = Vector2.left * offset;//
        //mr.SetVector("Offset", Vector2.left*offset);
    }
}
