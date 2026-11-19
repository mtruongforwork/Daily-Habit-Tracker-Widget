using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;

namespace HabitGrid.WinUI.Controls;

/// <summary>
/// Hệ thống chuyển động dùng chung (Composition, chạy trên luồng dựng hình nên mượt): xuất hiện lần lượt (mờ dần + nổi lên),
/// nâng nhẹ khi rê chuột và nhún khi bấm. Tôn trọng "giảm chuyển động" (Windows và Cài đặt của app): khi tắt thì mọi thứ hiện ngay.
/// </summary>
public static class Motion
{
    private const int EnterMilliseconds = 460;
    private const int StaggerMilliseconds = 55;
    private const float RiseDistance = 22f;

    public static bool Enabled => App.Host.Get<ThemeService>().AnimationsEnabled;

    /// <summary>Hàm ease thân thiện: nhanh lúc đầu, giảm tốc êm lúc cuối.</summary>
    private static CubicBezierEasingFunction Ease(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

    /// <summary>Phần tử hiện dần và nổi lên; <paramref name="index"/> tạo độ trễ để nhiều thẻ lần lượt xuất hiện.</summary>
    public static void Enter(UIElement element, int index = 0)
    {
        if (!Enabled)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 12) * StaggerMilliseconds);
        var duration = TimeSpan.FromMilliseconds(EnterMilliseconds);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, Ease(compositor));
        fade.Duration = duration;
        fade.DelayTime = delay;
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        var rise = compositor.CreateScalarKeyFrameAnimation();
        rise.InsertKeyFrame(0f, RiseDistance);
        rise.InsertKeyFrame(1f, 0f, Ease(compositor));
        rise.Duration = duration;
        rise.DelayTime = delay;
        rise.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation.Y", rise);
    }

    /// <summary>Cho các phần tử con trực tiếp của một khung xuất hiện lần lượt (bỏ qua phần tử đang ẩn).</summary>
    public static void EnterChildren(Panel panel)
    {
        var index = 0;

        foreach (var child in panel.Children)
        {
            if (child.Visibility == Visibility.Visible)
            {
                Enter(child, index++);
            }
        }
    }

    /// <summary>Nâng nhẹ khi rê chuột, nhún khi bấm. Gọi một lần cho mỗi phần tử (thẻ, nút lớn).</summary>
    public static void Interactive(FrameworkElement element, float hoverScale = 1.018f, float pressScale = 0.975f)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);

        void Center() => visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);

        element.SizeChanged += (_, _) => Center();

        void To(float scale, int ms)
        {
            if (!Enabled)
            {
                return;
            }

            var compositor = visual.Compositor;
            var animation = compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(1f, new Vector3(scale, scale, 1f), Ease(compositor));
            animation.Duration = TimeSpan.FromMilliseconds(ms);
            visual.StartAnimation("Scale", animation);
        }

        element.PointerEntered += (_, _) => To(hoverScale, 160);
        element.PointerExited += (_, _) => To(1f, 220);
        element.PointerPressed += (_, _) => To(pressScale, 90);
        element.PointerReleased += (_, _) => To(hoverScale, 140);
        element.PointerCanceled += (_, _) => To(1f, 160);
        element.PointerCaptureLost += (_, _) => To(1f, 160);
    }

    /// <summary>Gắn ImplicitAnimations cho Scale để thẻ kính phản hồi mượt khi hover/nhấn.</summary>
    public static void AttachGlassHover(FrameworkElement element, float hoverScale = 1.016f)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        element.SizeChanged += (_, _) =>
            visual.CenterPoint = new Vector3((float)element.ActualWidth / 2f, (float)element.ActualHeight / 2f, 0f);

        var spring = compositor.CreateSpringVector3Animation();
        spring.Target = "Scale";
        spring.DampingRatio = 0.65f;
        spring.Period = TimeSpan.FromMilliseconds(45);

        var implicitAnims = compositor.CreateImplicitAnimationCollection();
        implicitAnims["Scale"] = spring;
        visual.ImplicitAnimations = implicitAnims;

        element.PointerEntered += (_, _) =>
        {
            if (Enabled)
            {
                visual.Scale = new Vector3(hoverScale, hoverScale, 1f);
            }
        };
        element.PointerExited += (_, _) => visual.Scale = Vector3.One;
        element.PointerCanceled += (_, _) => visual.Scale = Vector3.One;
        element.PointerCaptureLost += (_, _) => visual.Scale = Vector3.One;
    }

    /// <summary>Hiệu ứng lò xo nhanh khi check-in, tăng số hoặc chạm Mochi.</summary>
    public static void SpringPop(FrameworkElement element, float fromScale = 0.92f)
    {
        if (!Enabled)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)Math.Max(element.ActualWidth, 1) / 2f, (float)Math.Max(element.ActualHeight, 1) / 2f, 0f);

        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.InitialValue = new Vector3(fromScale, fromScale, 1f);
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = 0.45f;
        spring.Period = TimeSpan.FromMilliseconds(48);
        visual.StartAnimation("Scale", spring);
    }

    /// <summary>Gợn sóng nhẹ từ điểm chạm khi check-in.</summary>
    public static void Ripple(Panel host, Windows.Foundation.Point origin, Windows.UI.Color color)
    {
        if (!Enabled)
        {
            return;
        }

        var ring = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 28,
            Height = 28,
            Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(color),
            StrokeThickness = 2.5,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Math.Max(0, origin.X - 14), Math.Max(0, origin.Y - 14), 0, 0)
        };
        host.Children.Add(ring);

        var visual = ElementCompositionPreview.GetElementVisual(ring);
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3(14f, 14f, 0f);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(0.5f, 0.5f, 1f));
        scale.InsertKeyFrame(1f, new Vector3(4.2f, 4.2f, 1f), Ease(compositor));
        scale.Duration = TimeSpan.FromMilliseconds(360);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0.75f);
        fade.InsertKeyFrame(1f, 0f, Ease(compositor));
        fade.Duration = TimeSpan.FromMilliseconds(360);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Scale", scale);
        visual.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => ring.DispatcherQueue.TryEnqueue(() => host.Children.Remove(ring));
    }
}
