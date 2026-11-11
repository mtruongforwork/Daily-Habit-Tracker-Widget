namespace HabitGrid.WidgetHost;

/// <summary>
/// Lò xo giảm chấn cho hoạt ảnh biến hình của widget (cảm giác "Dynamic Island": nhanh lúc đầu, vượt đích một chút rồi lắng lại).
/// Tích phân Euler nửa ẩn nên ổn định với bước thời gian nhỏ; không phụ thuộc WPF để kiểm tra được.
/// </summary>
public sealed class SpringValue
{
    /// <summary>Độ cứng (càng lớn càng nhanh).</summary>
    public double Stiffness { get; init; } = 380;

    /// <summary>Giảm chấn (càng nhỏ càng nảy). 38 với độ cứng 380 cho tỷ số giảm chấn ≈ 0,975: gần như không vượt đích; tới 98% quãng đường trong ~300 ms (kiểu Dynamic Island: nhanh mà êm).</summary>
    public double Damping { get; init; } = 38;

    public double Value { get; set; }

    public double Velocity { get; set; }

    public double Target { get; set; }

    /// <summary>Đã tới đích và đứng yên.</summary>
    public bool Settled => Math.Abs(Value - Target) < 0.05 && Math.Abs(Velocity) < 0.05;

    /// <summary>Đặt giá trị và đích cùng lúc, không chuyển động.</summary>
    public void Snap(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    /// <summary>Tiến một bước <paramref name="seconds"/> giây; trả về true nếu còn đang chuyển động.</summary>
    public bool Step(double seconds)
    {
        if (Settled)
        {
            Value = Target;
            Velocity = 0;
            return false;
        }

        // Chia nhỏ bước để lò xo cứng vẫn ổn định khi khung hình bị trễ.
        var remaining = Math.Min(seconds, 0.05);
        const double sub = 1.0 / 240;

        while (remaining > 0)
        {
            var dt = Math.Min(sub, remaining);
            var acceleration = (-Stiffness * (Value - Target)) - (Damping * Velocity);
            Velocity += acceleration * dt;
            Value += Velocity * dt;
            remaining -= dt;
        }

        if (Settled)
        {
            Value = Target;
            Velocity = 0;
            return false;
        }

        return true;
    }
}
