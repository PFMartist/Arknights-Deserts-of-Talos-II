using System;
internal static class OperatorLayoutFixture61
{
    static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    static void Drain(ProductionOperatorLayout61 driver){driver.Tick();driver.Tick();driver.Tick();}
    static int Main()
    {
        try
        {
            ProductionOperatorLayout61 driver=new ProductionOperatorLayout61();
            driver.Ready=true;driver.Root.activeInHierarchy=false;
            driver.Schedule();driver.Schedule();driver.Resize();
            Require(driver.CoroutineStarts==0&&!driver.AlignmentPending,"hidden page queued selection/resize work");
            driver.Align();Require(driver.AlignExecutions==0,"hidden built page performed direct layout");
            driver.Ready=false;driver.Align();Require(driver.AlignExecutions==1,"initial build lost pre-Ready geometry alignment");
            driver.Resize();Require(driver.BuildRequests==1,"initial screen resize no longer requests bootstrap");
            driver.Ready=true;driver.Root.activeInHierarchy=true;
            driver.Schedule();driver.Schedule();driver.Resize();
            Require(driver.CoroutineStarts==1&&driver.AlignmentPending,"same-frame selection/lead/resize did not coalesce");
            driver.Tick();Require(driver.AlignExecutions==1,"layout ran before first native-layout yield");
            driver.Tick();Require(driver.AlignExecutions==1,"layout ran before second native-layout yield");
            driver.Tick();Require(driver.AlignExecutions==2&&!driver.AlignmentPending,"first visible page did not align after native layout");
            driver.Resize();Drain(driver);Require(driver.AlignExecutions==3,"visible resize lost alignment");
            driver.Schedule();driver.Root.activeInHierarchy=false;Drain(driver);
            Require(driver.AlignExecutions==3&&!driver.AlignmentPending,"page hidden during pending coroutine still aligned or stayed pending");
            driver.Root.activeInHierarchy=true;driver.Schedule();Drain(driver);
            Require(driver.AlignExecutions==4,"shown page did not rearm alignment after a hidden pending request");
            driver.gameObject.activeInHierarchy=false;int starts=driver.CoroutineStarts;driver.Schedule();
            Require(driver.CoroutineStarts==starts,"inactive host started layout coroutine");
            driver.gameObject.activeInHierarchy=true;driver.Root=null;driver.Schedule();driver.Align();
            Require(driver.CoroutineStarts==starts&&driver.AlignExecutions==4,"missing root was not ignored");
            Console.WriteLine("PASS production ScheduleAlignment/AfterNativeLayout/OnScreenResize and AlignToNativeHangar guard prefix: hidden page no queue/write, same-frame requests coalesce, pre-Ready build, first show, visible resize, hide-while-pending, re-show and inactive/missing roots");
            Console.WriteLine("PASS offline scheduling/guard scope; geometry writes, native NGUI timing and actual game clicks remain runtime acceptance");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
