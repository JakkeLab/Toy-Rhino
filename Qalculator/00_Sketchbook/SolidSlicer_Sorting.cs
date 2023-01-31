using System;
using System.Collections;
using System.Collections.Generic;

using Rhino;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;



/// <summary>
/// This class will be instantiated on demand by the Script component.
/// </summary>
public class Script_Instance : GH_ScriptInstance
{
#region Utility functions
  /// <summary>Print a String to the [Out] Parameter of the Script component.</summary>
  /// <param name="text">String to print.</param>
  private void Print(string text) { __out.Add(text); }
  /// <summary>Print a formatted String to the [Out] Parameter of the Script component.</summary>
  /// <param name="format">String format.</param>
  /// <param name="args">Formatting parameters.</param>
  private void Print(string format, params object[] args) { __out.Add(string.Format(format, args)); }
  /// <summary>Print useful information about an object instance to the [Out] Parameter of the Script component. </summary>
  /// <param name="obj">Object instance to parse.</param>
  private void Reflect(object obj) { __out.Add(GH_ScriptComponentUtilities.ReflectType_CS(obj)); }
  /// <summary>Print the signatures of all the overloads of a specific method to the [Out] Parameter of the Script component. </summary>
  /// <param name="obj">Object instance to parse.</param>
  private void Reflect(object obj, string method_name) { __out.Add(GH_ScriptComponentUtilities.ReflectType_CS(obj, method_name)); }
#endregion

#region Members
  /// <summary>Gets the current Rhino document.</summary>
  private RhinoDoc RhinoDocument;
  /// <summary>Gets the Grasshopper document that owns this script.</summary>
  private GH_Document GrasshopperDocument;
  /// <summary>Gets the Grasshopper script component that owns this script.</summary>
  private IGH_Component Component; 
  /// <summary>
  /// Gets the current iteration count. The first call to RunScript() is associated with Iteration==0.
  /// Any subsequent call within the same solution will increment the Iteration count.
  /// </summary>
  private int Iteration;
#endregion

  /// <summary>
  /// This procedure contains the user code. Input parameters are provided as regular arguments, 
  /// Output parameters as ref arguments. You don't have to assign output parameters, 
  /// they will have a default value.
  /// </summary>
  private void RunScript(List<Guid> B, List<Surface> C, List<object> FL, ref object Vols)
  {
        List<Brep> breps = new List<Brep>();
    List<Brep> cutterBreps = new List<Brep>();
    List<Surface> sortedSrfs = new List<Surface>();
    List<double> unsortedOriginZ = new List<double>();
    List<double> sortedOriginZ = new List<double>();
    List<string> fLevels = new List<string>();
    List<Brep> newCappeds = new List<Brep>();
    Plane xyPlane = new Plane(0, 0, 1, 0);
    List<Interval> flRanges = new List<Interval>();
    List<double> BrepRange = new List<double>();
    List<int> containments = new List<int>();

    //DataTrees
    Grasshopper.DataTree<object> tree1 = new Grasshopper.DataTree<object>();
    Grasshopper.DataTree<double> tree2 = new Grasshopper.DataTree<double>();
    Grasshopper.DataTree<string> tree3 = new Grasshopper.DataTree<string>();
    //subtrees1
    List<GH_Path> subtrees1 = new List<GH_Path>();
    for (int i = 0; i < B.Count + 1; i++)
    {
      GH_Path newPath = new GH_Path(i);
      subtrees1.Add(newPath);
    }
    //subtrees2
    List<GH_Path> subtrees2 = new List<GH_Path>();
    for (int i = 0; i < B.Count + 1; i++)
    {
      GH_Path newPath = new GH_Path(i);
      subtrees2.Add(newPath);
    }
    //subtrees3
    List<GH_Path> subtrees3 = new List<GH_Path>();
    for (int i = 0; i < B.Count + 1; i++)
    {
      GH_Path newPath = new GH_Path(i);
      subtrees3.Add(newPath);
    }

    //Guid to Brep
    for(int i = 0; i < B.Count; i++)
    {
      var RhinoObject = RhinoDocument.Objects.Find(B[i]);
      var Geometry = RhinoObject.Geometry;
      Brep aBrep = Brep.TryConvertBrep(Geometry);                   //Brep으로 바꿔줘야 한다
      breps.Add(aBrep);
    }

    //Get Integrated brep of All input Brep
    Brep integrated = new Brep();
    for(int i = 0; i < breps.Count; i++)
    {
      integrated.Append(breps[i]);
    }

    //Get Z Axis Range of all Breps
    BoundingBox integratedBbox = integrated.GetBoundingBox(xyPlane);
    BrepRange.Add(integratedBbox.Min.Z);
    BrepRange.Add(integratedBbox.Max.Z);

    //Srf check and sort Srf, fLevels
    foreach(var item in C)
    {
      Point3d pOrigin = item.PointAt(0, 0);
      double sOriginZ = pOrigin.Z;
      unsortedOriginZ.Add(sOriginZ);
      sortedOriginZ.Add(sOriginZ);
    }

    sortedOriginZ.Sort();
    if(sortedOriginZ[0] > sortedOriginZ[sortedOriginZ.Count - 1])
    {
      sortedOriginZ.Reverse();
    }
    for(int i = 0; i < sortedOriginZ.Count; i++)
    {
      int idx = unsortedOriginZ.IndexOf(sortedOriginZ[i]);
      sortedSrfs.Add(C[idx]);
      fLevels.Add(FL[idx].ToString());
      BrepRange.Insert(i + 1, sortedOriginZ[i]);
    }
    fLevels.Insert(0, "BOTTOM");

    //Make Intervals
    for(int i = 0;i < BrepRange.Count - 1;i++)
    {
      Interval flRange = new Interval(BrepRange[i], BrepRange[i + 1]);
      flRanges.Add(flRange);
    }


    //Srf to Brep
    for(int i = 0; i < sortedSrfs.Count; i++)
    {
      Brep convertedCutter = sortedSrfs[i].ToBrep();
      Vector3d testNorm = sortedSrfs[i].NormalAt(0, 0);
      if(i == 0)
      {
        if(testNorm.Z > 0)
        {
          convertedCutter.Flip();
        }
      }
      else
      {
        if(testNorm.Z < 0)
        {
          convertedCutter.Flip();
        }
      }
      cutterBreps.Add(convertedCutter);
    }


    //BrepSplit, Cap and Sort into Array
    List<Brep> newCap = new List<Brep>();
    List<Brep> addToBreps = new List<Brep>();
    List<double> addToVols = new List<double>();
    List<string> addToVolsStr = new List<string>();
    for(int i = 0; i < breps.Count; i++)
    {
      addToBreps.Clear();
      addToVols.Clear();
      addToVolsStr.Clear();
      newCap.Clear();
      Brep[] splitted = breps[i].Split(cutterBreps, RhinoDocument.ModelAbsoluteTolerance);
      for(int j = 0; j < splitted.Length; j++)
      {
        Brep Capped = splitted[j].CapPlanarHoles(RhinoDocument.ModelAbsoluteTolerance);
        newCap.Add(Capped);
      }
      List<int> b = brepIndexer(flRanges, newCap);

      addToBreps = ListFiller(newCap, b, flRanges.Count);
      addToVols = BrepVolChanger(addToBreps);
      addToVolsStr = BrepVolChangerStr(addToBreps);

      //Layer name extract
      var obj = RhinoDocument.Objects.Find(B[i]);
      string brepLayerName = RhinoDocument.Layers[obj.Attributes.LayerIndex].ToString();
      addToVolsStr.Insert(0, brepLayerName);
      tree1.AddRange(addToBreps, subtrees1[i]);
      tree2.AddRange(addToVols, subtrees2[i]);
      tree3.AddRange(addToVolsStr, subtrees3[i + 1]);
    }
    fLevels.Insert(0, "층 및 암종류");
    tree3.AddRange(fLevels, subtrees3[0]);
    Vols = tree3;
  }

  // <Custom additional code> 
    public double brepCenZ(Brep item)
  {
    Plane newPlane = new Plane(0, 0, 1, 0);
    BoundingBox newBox = item.GetBoundingBox(newPlane);
    Point3d bboxCen = (newBox.Min + newBox.Max) / 2;
    double zValue = bboxCen.Z;
    return zValue;
  }

  public List<int> brepIndexer(List<Interval> intervals, List<Brep> Breps)
  {
    List<int> IdxBox = new List<int>();
    int idx = -1;
    for(int i = 0; i < Breps.Count; i++)
    {
      double a = brepCenZ(Breps[i]);
      for(int j = 0; j < intervals.Count; j++)
      {
        if(intervals[j].IncludesParameter(a))
        {
          idx = j;
          IdxBox.Add(j);
        }
      }
    }
    return IdxBox;
  }

  public List<Brep> ListFiller (List<Brep> Breps, List<int> Indexes, int k)
  {
    List<Brep> newList = new List<Brep>();
    for(int i = 0; i < k; i++)
    {
      newList.Add(null);
    }
    for(int i = 0; i < Breps.Count; i++)
    {
      newList[Indexes[i]] = Breps[i];
    }
    return newList;
  }
  public List<double> BrepVolChanger(List<Brep> FilledList)
  {
    double vol = 0;
    List<double> Vols = new List<double>();
    foreach(var item in FilledList)
    {
      if(item != null)
      {
        vol = item.GetVolume();
      }
      else
      {
        vol = 0;
      }
      Vols.Add(vol);
    }
    return Vols;
  }

  public List<string> BrepVolChangerStr(List<Brep> FilledList)
  {
    double vol = 0;
    List<string> Vols = new List<string>();
    foreach(var item in FilledList)
    {
      if(item != null)
      {
        vol = item.GetVolume();
      }
      else
      {
        vol = 0;
      }
      Vols.Add(vol.ToString("F2"));
    }
    return Vols;
  }
  // </Custom additional code> 

  private List<string> __err = new List<string>(); //Do not modify this list directly.
  private List<string> __out = new List<string>(); //Do not modify this list directly.
  private RhinoDoc doc = RhinoDoc.ActiveDoc;       //Legacy field.
  private IGH_ActiveObject owner;                  //Legacy field.
  private int runCount;                            //Legacy field.
  
  public override void InvokeRunScript(IGH_Component owner, object rhinoDocument, int iteration, List<object> inputs, IGH_DataAccess DA)
  {
    //Prepare for a new run...
    //1. Reset lists
    this.__out.Clear();
    this.__err.Clear();

    this.Component = owner;
    this.Iteration = iteration;
    this.GrasshopperDocument = owner.OnPingDocument();
    this.RhinoDocument = rhinoDocument as Rhino.RhinoDoc;

    this.owner = this.Component;
    this.runCount = this.Iteration;
    this. doc = this.RhinoDocument;

    //2. Assign input parameters
        List<Guid> B = null;
    if (inputs[0] != null)
    {
      B = GH_DirtyCaster.CastToList<Guid>(inputs[0]);
    }
    List<Surface> C = null;
    if (inputs[1] != null)
    {
      C = GH_DirtyCaster.CastToList<Surface>(inputs[1]);
    }
    List<object> FL = null;
    if (inputs[2] != null)
    {
      FL = GH_DirtyCaster.CastToList<object>(inputs[2]);
    }


    //3. Declare output parameters
      object Vols = null;


    //4. Invoke RunScript
    RunScript(B, C, FL, ref Vols);
      
    try
    {
      //5. Assign output parameters to component...
            if (Vols != null)
      {
        if (GH_Format.TreatAsCollection(Vols))
        {
          IEnumerable __enum_Vols = (IEnumerable)(Vols);
          DA.SetDataList(0, __enum_Vols);
        }
        else
        {
          if (Vols is Grasshopper.Kernel.Data.IGH_DataTree)
          {
            //merge tree
            DA.SetDataTree(0, (Grasshopper.Kernel.Data.IGH_DataTree)(Vols));
          }
          else
          {
            //assign direct
            DA.SetData(0, Vols);
          }
        }
      }
      else
      {
        DA.SetData(0, null);
      }

    }
    catch (Exception ex)
    {
      this.__err.Add(string.Format("Script exception: {0}", ex.Message));
    }
    finally
    {
      //Add errors and messages... 
      if (owner.Params.Output.Count > 0)
      {
        if (owner.Params.Output[0] is Grasshopper.Kernel.Parameters.Param_String)
        {
          List<string> __errors_plus_messages = new List<string>();
          if (this.__err != null) { __errors_plus_messages.AddRange(this.__err); }
          if (this.__out != null) { __errors_plus_messages.AddRange(this.__out); }
          if (__errors_plus_messages.Count > 0) 
            DA.SetDataList(0, __errors_plus_messages);
        }
      }
    }
  }
}
