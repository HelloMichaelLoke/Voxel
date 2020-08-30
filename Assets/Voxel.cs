public struct Voxel
{
    private sbyte density;
    private byte material;

    public Voxel(sbyte density, byte material)
    {
        this.density = density;
        this.material = material;
    }

    // Set Voxel

    public void SetVoxel(sbyte density, byte material)
    {
        this.density = density;
        this.material = material;
    }

    //
    // Set & Get Material
    //

    public void SetMaterial(byte material)
    {
        this.material = material;
    }

    public byte GetMaterial()
    {
        return this.material;
    }

    //
    // Set & Get Density
    //

    public void SetDensity(sbyte density)
    {
        this.density = density;
    }

    public sbyte GetDensity()
    {
        return this.density;
    }

    //
    // Helpers
    //

    public bool IsSolid()
    {
        return (this.density < 0);
    }
}