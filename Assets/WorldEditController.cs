using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldEditController : MonoBehaviour
{
    public World world;
    public Camera mainCamera;
    public GameObject visualizer;
    public GameObject visualizerClosest;

    void Start()
    {
        
    }

    void Update()
    {
        RaycastHit hit;
        Ray ray = this.mainCamera.ScreenPointToRay(Input.mousePosition);
        int layerMask = 1 << 8;

        if (Physics.Raycast(ray, out hit, 10.0f, layerMask))
        {
            Transform objectHit = hit.transform;

            if (objectHit.tag == "Terrain")
            {
                this.visualizer.SetActive(true);
                this.visualizer.transform.position = hit.point;

                //this.visualizerClosest.SetActive(true);
                //this.visualizerClosest.transform.position = worldPosition;

                if (Input.GetMouseButton(0))
                {
                    Vector3 worldPosition = this.world.GetClosestVoxelWorldPosition(hit.point, false);
                    this.world.WorldEditDraw(worldPosition, -63, 3);
                }
                else if (Input.GetMouseButton(1))
                {
                    Vector3 worldPosition = this.world.GetClosestVoxelWorldPosition(hit.point, true);
                    this.world.WorldEditErase(worldPosition);
                }
            }
        }
        else
        {
            this.visualizer.SetActive(false);
            this.visualizerClosest.SetActive(false);
        }
    }
}
