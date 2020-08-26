using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WorldEdit
{
    public enum SelectionMode
    {
        Distance = 0,
        Raycast = 1
    }

    public enum EditMode
    {
        Draw = 0,
        Erase = 1,
        Add = 2,
        Substract = 3,
        Paint = 4
    }

    public enum ApplyMode
    {
        Click = 0,
        Hold = 1
    }
}

public class WorldEditController : MonoBehaviour
{
    public GameObject worldEditWindow;

    public bool isWorldEditEnabled = false;

    public World world;
    public Camera mainCamera;
    public GameObject visualizer;
    public GameObject visualizerClosest;

    private WorldEdit.SelectionMode selectionMode;
    private WorldEdit.EditMode editMode;
    private WorldEdit.ApplyMode applyMode;

    private Vector3 selectionPosition;
    private bool isApplying;

    public void Start()
    {
        this.selectionMode = WorldEdit.SelectionMode.Raycast;
        this.editMode = WorldEdit.EditMode.Add;
        this.applyMode = WorldEdit.ApplyMode.Click;
    }

    public void Update()
    {
        //
        // Test Modes
        //

        if (Input.GetKeyDown(KeyCode.Q))
        {
            this.EnableWindow();
        }

        /*
        if (Input.GetKeyDown(KeyCode.Q))
        {
            if (this.selectionMode == WorldEdit.SelectionMode.Raycast)
            {
                this.SetSelectionMode(WorldEdit.SelectionMode.Distance);
            }
            else if (this.selectionMode == WorldEdit.SelectionMode.Distance)
            {
                this.SetSelectionMode(WorldEdit.SelectionMode.Raycast);
            }
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (this.applyMode == WorldEdit.ApplyMode.Click)
            {
                this.SetApplyMode(WorldEdit.ApplyMode.Hold);
            }
            else if (this.applyMode == WorldEdit.ApplyMode.Hold)
            {
                this.SetApplyMode(WorldEdit.ApplyMode.Click);
            }
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            this.SetEditMode(WorldEdit.EditMode.Draw);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            this.SetEditMode(WorldEdit.EditMode.Erase);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            this.SetEditMode(WorldEdit.EditMode.Add);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            this.SetEditMode(WorldEdit.EditMode.Substract);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            this.SetEditMode(WorldEdit.EditMode.Paint);
        }
        */

        //
        // Return if world edit is disabled
        //

        if (!this.isWorldEditEnabled || Cursor.visible == true)
        {
            this.visualizer.SetActive(false);
            this.visualizerClosest.SetActive(false);
            return;
        }
        else
        {
            this.visualizer.SetActive(true);
            this.visualizerClosest.SetActive(true);
        }

        //
        // Find selection position
        //

        this.selectionPosition = Vector3.down;

        if (this.selectionMode == WorldEdit.SelectionMode.Distance)
        {
            selectionPosition = this.mainCamera.transform.position + this.mainCamera.transform.forward * 3.0f;
        }
        else if (this.selectionMode == WorldEdit.SelectionMode.Raycast)
        {
            RaycastHit hit;
            Ray ray = this.mainCamera.ScreenPointToRay(Input.mousePosition);
            int layerMask = 1 << 8;

            if (Physics.Raycast(ray, out hit, 120.0f, layerMask))
            {
                Transform objectHit = hit.transform;

                if (objectHit.tag == "Terrain")
                {
                    selectionPosition = hit.point;
                }
            }
        }

        //
        // Return if selection position wasn't set
        //

        if (this.selectionPosition == Vector3.down)
        {
            return;
        }

        //
        // Detect if is applying
        //

        this.isApplying = false;

        if (this.applyMode == WorldEdit.ApplyMode.Click)
        {
            if (Input.GetMouseButtonDown(0))
            {
                this.isApplying = true;
            }
        }
        else if (this.applyMode == WorldEdit.ApplyMode.Hold)
        {
            if (Input.GetMouseButton(0))
            {
                this.isApplying = true;
            }
        }

        //
        // Apply
        //

        if (isApplying)
        {
            if (this.editMode == WorldEdit.EditMode.Draw)
            {
                this.world.WorldEditDraw(selectionPosition, 1);
            }
            else if (this.editMode == WorldEdit.EditMode.Erase)
            {
                this.world.WorldEditErase(selectionPosition);
            }
            else if (this.editMode == WorldEdit.EditMode.Add)
            {
                this.world.WorldEditAdd(selectionPosition, 2);
            }
            else if (this.editMode == WorldEdit.EditMode.Substract)
            {
                this.world.WorldEditSubstract(selectionPosition, 2);
            }
            else if (this.editMode == WorldEdit.EditMode.Paint)
            {
                this.world.WorldEditPaint(selectionPosition, 1);
            }
        }

        //
        // Update Visualizer
        //

        bool findSolid = true;

        if (this.editMode == WorldEdit.EditMode.Draw)
        {
            findSolid = false;
        }
        else if (this.editMode == WorldEdit.EditMode.Erase)
        {
            findSolid = true;
        }
        else if (this.editMode == WorldEdit.EditMode.Add)
        {
            findSolid = true;
        }
        else if (this.editMode == WorldEdit.EditMode.Substract)
        {
            findSolid = true;
        }

        EditPosition closestEditPosition = this.world.GetClosestEditPosition(selectionPosition, findSolid);
        this.visualizer.transform.position = selectionPosition;
        this.visualizerClosest.transform.position = closestEditPosition.roundedPosition;
    }

    public void SetSelectionMode(WorldEdit.SelectionMode selectionMode)
    {
        this.selectionMode = selectionMode;
    }

    public void SetEditMode(WorldEdit.EditMode editMode)
    {
        this.editMode = editMode;
    }

    public void SetApplyMode(WorldEdit.ApplyMode applyMode)
    {
        this.applyMode = applyMode;
    }

    //
    // UI Window
    //

    public void EnableWindow()
    {
        this.worldEditWindow.SetActive(true);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.Confined;
    }

    public void DisableWindow()
    {
        this.worldEditWindow.SetActive(false);
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    // 
    // Button Functions
    //

    public void SetEditModeDraw()
    {
        this.SetEditMode(WorldEdit.EditMode.Draw);
        this.SetApplyMode(WorldEdit.ApplyMode.Click);
    }

    public void SetEditModeErase()
    {
        this.SetEditMode(WorldEdit.EditMode.Erase);
        this.SetApplyMode(WorldEdit.ApplyMode.Click);
    }

    public void SetEditModeAdd()
    {
        this.SetEditMode(WorldEdit.EditMode.Add);
        this.SetApplyMode(WorldEdit.ApplyMode.Hold);
    }

    public void SetEditModeSubstract()
    {
        this.SetEditMode(WorldEdit.EditMode.Substract);
        this.SetApplyMode(WorldEdit.ApplyMode.Hold);
    }

    public void SetEditModePaint()
    {
        this.SetEditMode(WorldEdit.EditMode.Paint);
        this.SetApplyMode(WorldEdit.ApplyMode.Hold);
    }
}
