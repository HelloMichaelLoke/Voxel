using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldEditController : MonoBehaviour
{
    public bool isWorldEditEnabled = false;

    public World world;
    public Camera mainCamera;
    public GameObject visualizer;
    public GameObject visualizerClosest;

    public void Start()
    {
        
    }

    public void Update()
    {
        if (!this.isWorldEditEnabled)
        {
            return;
        }

        /*
        Vector3 editPosition = this.mainCamera.transform.position + this.mainCamera.transform.forward * 3.0f;

        this.visualizer.transform.position = editPosition;

        EditPosition closestEditPosition = this.world.GetClosestEditPosition(editPosition, true);
        this.visualizerClosest.transform.position = closestEditPosition.roundedPosition;

        Vector3 relativeDirection = editPosition - closestEditPosition.roundedPosition;
        if (relativeDirection.x == 0.0f && relativeDirection.y == 0.0f && relativeDirection.z == 0.0f) relativeDirection.y = 1.0f;

        float absX = Mathf.Abs(relativeDirection.x);
        float absY = Mathf.Abs(relativeDirection.y);
        float absZ = Mathf.Abs(relativeDirection.z);

        Vector3Int direction = new Vector3Int(0, 0, 0);

        if (absX >= absY && absX >= absZ) direction.x = (int)Mathf.Sign(relativeDirection.x);
        else if (absY >= absX && absY >= absZ) direction.y = (int)Mathf.Sign(relativeDirection.y);
        else if (absZ >= absX && absZ >= absY) direction.z = (int)Mathf.Sign(relativeDirection.z);

        line.SetPositions(new Vector3[] { closestEditPosition.roundedPosition, closestEditPosition.roundedPosition + direction });

        
        if (Input.GetMouseButton(0))
        {
            this.world.WorldEditAdd(editPosition, 1.0f / 255.0f);
        }
        else if (Input.GetMouseButton(1))
        {
            this.world.WorldEditSubstract(editPosition, 1.0f / 255.0f);
        }
        */



        RaycastHit hit;
        Ray ray = this.mainCamera.ScreenPointToRay(Input.mousePosition);
        int layerMask = 1 << 8;

        if (Physics.Raycast(ray, out hit, 120.0f, layerMask))
        {
            Transform objectHit = hit.transform;

            if (objectHit.tag == "Terrain")
            {
                this.visualizer.SetActive(true);
                this.visualizer.transform.position = hit.point;

                if (Vector3.Distance(hit.point, this.transform.position) >= 1.0f)
                {
                    if (Input.GetMouseButtonDown(0))
                    {
                        this.world.WorldEditDraw(hit.point, (byte)3);
                    }
                    else if (Input.GetMouseButtonDown(1))
                    {
                        this.world.WorldEditErase(hit.point);
                    }
                }

                this.visualizerClosest.SetActive(true);
                this.visualizerClosest.transform.position = Vector3Int.RoundToInt(hit.point);
            }
        }
        else
        {
            this.visualizer.SetActive(false);
            this.visualizerClosest.SetActive(false);
        }
    }
}
