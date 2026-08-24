using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public class VeterancyChevron : MonoBehaviour
{
    Unit unit;
    setOpacity opac;
    public int iter;
    // Start is called before the first frame update
    void Start() {
        opac = GetComponent<setOpacity>();
        unit = GetComponentInParent<Unit>();
        try {
            iter = Int32.Parse(Regex.Match(gameObject.name, @"\d+").Value);
        } catch (FormatException e) {
            iter = 0;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (unit.Veterancy > iter) {
            opac.alpha = 1;
        } else {
            opac.alpha = 0;
        }
   }
}
