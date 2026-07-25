using HackyMessage.Metric;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class SlidingIqrOutlierDetectorTest
{
    
    [Test]
    [Category("Unit")]
    [Description("Warms up the size based percentile estimation logic then asserts that outliers are detected")]
    public void IsOutlier_ShouldDetectOutliers_WhenWarmedUp()
    {
        //setup
        var detector = new SlidingIqrOutlierDetector();
        WarmUp(detector, 950, 1050, 200);
        
        //assert normal and outlier values
        Assert.That(detector.IsOutlier(1051), Is.False);
        Assert.That(detector.IsOutlier(949), Is.False);
        
        Assert.That(detector.IsOutlier(2000), Is.True);
        Assert.That(detector.IsOutlier(500), Is.True);
    }
    
    [Test]
    [Category("Unit")]
    [Description("Warms up the size based percentile estimation logic then asserts that outliers are detected")]
    public void IsBigOutlier_ShouldDetectBigOutliers_WhenWarmedUp()
    {
        //setup
        var detector = new SlidingIqrOutlierDetector();
        WarmUp(detector, 950, 1050, 200);
        
        //assert normal and outlier values
        Assert.That(detector.IsBigOutlier(1051), Is.False);
        Assert.That(detector.IsBigOutlier(949), Is.False);
        Assert.That(detector.IsBigOutlier(500), Is.False);
        
        Assert.That(detector.IsBigOutlier(2000), Is.True);
    }
    
    [Test]
    [Category("Unit")]
    [Description("Warms up the size based percentile estimation logic then asserts that outliers are detected")]
    public void IsSmallOutlier_ShouldDetectSmallOutliers_WhenWarmedUp()
    {
        //setup
        var detector = new SlidingIqrOutlierDetector();
        WarmUp(detector, 950, 1050, 200);
        
        //assert normal and outlier values
        Assert.That(detector.IsSmallOutlier(1051), Is.False);
        Assert.That(detector.IsSmallOutlier(949), Is.False);
        Assert.That(detector.IsSmallOutlier(2000), Is.False);
        
        Assert.That(detector.IsSmallOutlier(500), Is.True);
    }
    
    [Test]
    [Category("Unit")]
    [Description("Warms up the size based percentile estimation logic then asserts that the outlier detection is adaptive")]
    public void IsOutlier_ShouldAdaptToObservedValues_WhenWarmedUpToDifferentRanges()
    {
        //setup & first warmup (1000 +/- 50)
        var detector = new SlidingIqrOutlierDetector();
        WarmUp(detector, 950, 1050, 200);
        
        //assert normal and outlier value
        Assert.That(detector.IsOutlier(1000), Is.False);
        Assert.That(detector.IsOutlier(2000), Is.True);
        
        //second warmup (2000 +/- 50)
        WarmUp(detector, 1950, 2050, 200);
        
        //assert normal and outlier value have switched
        Assert.That(detector.IsOutlier(1000), Is.True);
        Assert.That(detector.IsOutlier(2000), Is.False);
    }

    private static void WarmUp(SlidingIqrOutlierDetector detector, int min, int max, int count)
    {
        var random = new Random(42);
        for (var i = 0; i < count; i++)
            detector.Observe(random.Next(min, max));
    }
}