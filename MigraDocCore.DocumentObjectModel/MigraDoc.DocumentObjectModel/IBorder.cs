namespace MigraDocCore.DocumentObjectModel
{
    public interface IBorder
    {
        Unit Width { get; set; }
        Color Color { get; set; }
        BorderStyle? Style { get; set; }
        bool? Visible { get; set; }
    }
}