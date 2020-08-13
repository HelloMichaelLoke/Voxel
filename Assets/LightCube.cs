using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LightCube : MonoBehaviour
{
    public World world;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        this.GetComponent<MeshRenderer>().material.SetFloat("_Brightness", this.world.GetLightValue(this.transform.position));
    }
}
