using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TerrainGeneration;
using Seb.Meshing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Reflection;
using System;
using Dynamitey;
using UnityEngine.UI;
using SFB;
using System.IO;

public class GlobeMapCreator : Generator
{
	[Header("Settings")]
	public int resolution = 100;
	public int oceanResolution = 50;
	public float minRaiseHeight;
	public float raiseHeightMultiplier;
	public float radius = 10;
	public float tinyAreaThreshold;

	[Header("References")]
	public Material countryMaterial;
	public Material oceanMaterial;
	public CountryLoader countryLoader;
	public TextAsset averageHeightFile;

	[Header("Save/Load")]
	public string countriesSaveFileName;
	public string oceanSaveFileName;
	public TextAsset loadFileCountries;
	public TextAsset loadFileOcean;

	Vector3[] spherePoints;
	Coordinate[] spherePoints2D;

	Dictionary<string, float> averageCountryElevations;
	float maxElevation;
	SimpleMeshData[] allCountriesMeshData;
	SimpleMeshData oceanMeshData;

	public GameObject particleSystemPrefab;
	public Transform particlesParent;

	public TextAsset literacyFile;
	public TextAsset energyConsumptionFile;

	public TextAsset populationFile;


	public TMPro.TMP_Dropdown literacyDropdown;


	public Slider slider = null;
	public TMPro.TextMeshProUGUI literacyYear;

	string[] a = new string[] { "_1960", "_1961", "_1962", "_1963", "_1964", "_1965", "_1966", "_1967", "_1968", "_1969", "_1970", "_1971", "_1972", "_1973", "_1974", "_1975", "_1976", "_1977", "_1978", "_1979", "_1980", "_1981", "_1982", "_1983", "_1984", "_1985", "_1986", "_1987", "_1988", "_1989", "_1990", "_1991", "_1992", "_1993", "_1994", "_1995", "_1996", "_1997", "_1998", "_1999", "_2000", "_2001", "_2002", "_2003", "_2004", "_2005", "_2006", "_2007", "_2008", "_2009", "_2010", "_2011", "_2012", "_2013", "_2014", "_2015", "_2016", "_2017", "_2018", "_2019", "_2020", "_2021" };

	List<string> yearList;
	public GameObject loadingPanel;

	public GameObject showLineChartButton;
	public UpdateDataInChart chart;

	public string currentFileName;
	public string currentFileContent;

	private void Start()
    {
		showLineChartButton.SetActive(false);
		slider.onValueChanged.AddListener(delegate { SliderValueChangedCallback(); });
		yearList  = a.ToList();

		Load();
	}
	public TMPro.TextMeshProUGUI title;
	public void OpenFile()
	{
		//clear everything before loading new data
		currentFileContent = string.Empty;
		currentFileName = string.Empty;

		loadingPanel.SetActive(true);
		allCountriesData = new List<LoadedCountryData>();
		chart.ClearData();

		foreach (GameObject g in allCountries)
		{
			g.gameObject.GetComponent<GenerateRandomPoint>().ClearEnergyParticles();
			g.gameObject.GetComponent<GenerateRandomPoint>().ClearWageParticles();
			g.gameObject.GetComponent<GenerateRandomPoint>().ClearLiteracyParticles();
		}

		InstantiateScrollView.instance.DeleteAll();
		title.text = "";

		// Open file with filter
		var extensions = new[] {
		new ExtensionFilter("Database", "txt"),
		};
		var paths = StandaloneFileBrowser.OpenFilePanel("Open File", "", extensions, true);
		if (paths.Length > 0)
		{
			currentFileContent = File.ReadAllText(new System.Uri(paths[0]).AbsolutePath);
			currentFileName = System.IO.Path.GetFileNameWithoutExtension(paths[0]);
			title.text = currentFileName;
			YearToColorsList();
			LoadAllYears(File.ReadAllText(new System.Uri(paths[0]).AbsolutePath));
		}
	}

	public void ShowChart()
    {
		chart.gameObject.SetActive(true);
		//chart.ShowDataFromString(currentFileContent, currentFileName);
		chart.ShowDataFromClass(allCountriesData, currentFileName);
	}

	private void SliderValueChangedCallback()
	{
		// grab out numeric value of the slider - cast to int as the value should be a whole number
		int numericSliderValue = (int)slider.value;
		literacyYear.text = numericSliderValue.ToString();

		InstantiateParticlesAsPerYear("_"+numericSliderValue.ToString());
	}

	public override void StartGenerating()
	{
		NotifyGenerationStarted();
		StartCoroutine(Generate());
	}
	public List<GameObject> allCountries;
	IEnumerator Generate()
	{
		allCountries = new List<GameObject>();
		// Create ocean mesh
		oceanMeshData = IcoSphere.Generate(oceanResolution, radius);
		MeshHelper.CreateRendererObject("Ocean", oceanMeshData, oceanMaterial, transform);

		Country[] countries = countryLoader.GetCountries();
		allCountriesMeshData = new SimpleMeshData[countries.Length];

		spherePoints = IcoSphere.Generate(resolution).vertices;
		spherePoints2D = new Coordinate[spherePoints.Length];
		for (int i = 0; i < spherePoints.Length; i++)
		{
			spherePoints2D[i] = GeoMaths.PointToCoordinate(spherePoints[i]);
		}

		// Load average heights
		averageCountryElevations = new Dictionary<string, float>();
		maxElevation = 0;
		string[] entries = averageHeightFile.text.Split('\n');
		foreach (string entry in entries)
		{
			string[] data = entry.Split(',');
			float height = float.Parse(data[1]);
			averageCountryElevations.Add(data[0], height);
			maxElevation = Mathf.Max(maxElevation, height);
		}

		// Create country meshes
		for (int i = 0; i < countries.Length; i++)
		{
			SimpleMeshData countryMeshData = GenerateCountry(countries[i]);
			string countryName = countries[i].GetPreferredDisplayName();
			countryMeshData.name = countryName;
			RenderObject obj = MeshHelper.CreateRendererObject(countryName, countryMeshData, countryMaterial, transform);
			allCountries.Add(obj.gameObject);
			allCountriesMeshData[i] = countryMeshData;
			yield return null;
		}

		Debug.Log("Generation Complete");
		NotifyGenerationComplete();
		AddLiteracyDropdown();
	}
	public override void Save()
	{
		// Save countries
		foreach (var meshData in allCountriesMeshData)
		{
			meshData.Optimize();
		}

		byte[] bytes = MeshSerializer.MeshesToBytes(allCountriesMeshData);
		FileHelper.SaveBytesToFile(SavePath, countriesSaveFileName, bytes, log: true);

		// Save ocean
		oceanMeshData.Optimize();
		byte[] oceanBytes = MeshSerializer.MeshToBytes(oceanMeshData);
		FileHelper.SaveBytesToFile(SavePath, oceanSaveFileName, oceanBytes, log: true);
	}

	public override void Load()
	{
		SimpleMeshData[] meshes = MeshSerializer.BytesToMeshes(loadFileCountries.bytes);
		for (int i = 0; i < meshes.Length; i++)
		{
			RenderObject obj = MeshHelper.CreateRendererObject(meshes[i].name, meshes[i], countryMaterial, transform);
			allCountries.Add(obj.gameObject);
		}
        //SimpleMeshData[] oceanMesh = MeshSerializer.BytesToMeshes(loadFileOcean.bytes);
        //for (int i = 0; i < oceanMesh.Length; i++)
        //{
        //    RenderObject obj = MeshHelper.CreateRendererObject(oceanMesh[i].name, oceanMesh[i], oceanMaterial, transform);
        //}

        foreach (GameObject g in allCountries)
		{
			g.gameObject.AddComponent<GenerateRandomPoint>();
			g.gameObject.AddComponent<CountryHover>();
			g.gameObject.GetComponent<GenerateRandomPoint>().SetValues(particleSystemPrefab, particlesParent);
		}

		AddLiteracyDropdown();
	}

	private void AddLiteracyDropdown()
    {
		literacyDropdown.ClearOptions();

		literacyDropdown.AddOptions(yearList);

		literacyDropdown.onValueChanged.AddListener(delegate {
			DropdownValueChanged(literacyDropdown);
		});
	}

	void DropdownValueChanged(TMPro.TMP_Dropdown change)
	{
		Debug.Log("change:" + change.options[change.value].text);

		string year = change.options[change.value].text;

		//InstantiateParticlesAsPerYear(year);
		PopulatePopulation(year);
	}

	Color randomCol;

	List<Color> yearColors = new List<Color>();

	private void YearToColorsList()
	{
		foreach (string year in yearList)
		{
			Color col;
			//var random = new System.Random();
			//var color = String.Format("#{0:X6}", random.Next(0x1000000));
			//ColorUtility.TryParseHtmlString(color + "ff", out col);

			string s = year.Replace("_", "");

			col = UnityEngine.Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f);

			InstantiateScrollView.instance.Set(col, s);
			yearColors.Add(col);
		}
	}

	public void ShowYear(GameObject g)
    {
        if (g.GetComponent<Toggle>().isOn)
        {
			foreach (GameObject s in allCountries)
			{
				s.GetComponent<GenerateRandomPoint>().ClearEnergyParticles();
				s.GetComponent<GenerateRandomPoint>().ClearLiteracyParticles();
				s.GetComponent<GenerateRandomPoint>().ClearWageParticles();
			}

			string year = g.transform.parent.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>().text;
			year = "_" + year;
			if (!string.IsNullOrEmpty(year))
			{
				foreach (LoadedCountryData country in allCountriesData)
				{

					foreach (KeyValuePair<string, float> kvp in country.values)
					{

						foreach (GameObject s in allCountries)
						{
							if (country.countryName == s.name && kvp.Key == year)
							{
								s.GetComponent<GenerateRandomPoint>().GeneratePoints(Color.yellow, kvp.Key, (int)kvp.Value, currentFileName, true);
							}
						}
					}
				}
			}
		}
    }

	public List<LoadedCountryData> allCountriesData;
	void LoadAllYears(string fileContents)
	{
		allCountriesData = new List<LoadedCountryData>();

		Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(fileContents);

		loadingPanel.SetActive(true);

		for (int i = 0; i < myDeserializedClass.Data.Count; i++)
		{	
			foreach (GameObject s in allCountries)
			{
				if (s.name == myDeserializedClass.Data[i].CountryName)
                {
					LoadedCountryData country = new LoadedCountryData();
					country.values = new Dictionary<string, float>();
					country.countryName = s.name;
					
					int a = 0;
					foreach(string year in yearList)
                    {
						float value = 0f;
						if (Dynamic.InvokeGet(myDeserializedClass.Data[i], year) != null)
						{
							float.TryParse(Dynamic.InvokeGet(myDeserializedClass.Data[i], year), out value);
						}
						country.values.Add(year, value);

						//s.GetComponent<GenerateRandomPoint>().ClearLiteracyParticles();
						//s.GetComponent<GenerateRandomPoint>().ClearEnergyParticles();
						//s.GetComponent<GenerateRandomPoint>().ClearWageParticles();

						//s.GetComponent<GenerateRandomPoint>().GeneratePoints(yearColors[a], year, (int)value, "literacy",true);
						a++;
					}
					allCountriesData.Add(country);
				}
			}
		}
		loadingPanel.SetActive(false);
		showLineChartButton.SetActive(true);
	}

	void InstantiateParticlesAsPerYear(string year)
    {
				return;
		//literacy
        {
			Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(literacyFile.ToString());

			Debug.Log("data.count" + myDeserializedClass.Data.Count);

			for (int i = 0; i < myDeserializedClass.Data.Count; i++)
			{
				if (GameObject.Find(myDeserializedClass.Data[i].CountryName))
				{
					GameObject country = GameObject.Find(myDeserializedClass.Data[i].CountryName);
					float value = 0f;
					try
					{
						if (Dynamic.InvokeGet(myDeserializedClass.Data[i], year) != null)
						{
							float.TryParse(Dynamic.InvokeGet(myDeserializedClass.Data[i], year), out value);
						}
						//Debug.Log("dynamic test:" + Dynamic.InvokeGet(myDeserializedClass.Data[i], year));
						//float.TryParse(myDeserializedClass.Data[i]._2018, out value);

						//country.GetComponent<GenerateRandomPoint>().ClearLiteracyParticles();
						//country.GetComponent<GenerateRandomPoint>().ClearEnergyParticles();
						//country.GetComponent<GenerateRandomPoint>().ClearWageParticles();
						country.GetComponent<GenerateRandomPoint>().GeneratePoints(randomCol,year, (int)value, "literacy");
					}
					catch (Exception)
					{

						throw;
					}

				}
			}
		}

		return;
		//energy
		{
			Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(energyConsumptionFile.ToString());

			Debug.Log("data.count" + myDeserializedClass.Data.Count);

			for (int i = 0; i < myDeserializedClass.Data.Count; i++)
			{
				if (GameObject.Find(myDeserializedClass.Data[i].CountryName))
				{
					GameObject country = GameObject.Find(myDeserializedClass.Data[i].CountryName);
					float value = 0f;
					try
					{
						if (Dynamic.InvokeGet(myDeserializedClass.Data[i], year) != null)
						{
							float.TryParse(Dynamic.InvokeGet(myDeserializedClass.Data[i], year), out value);
						}
						//Debug.Log("dynamic test:" + Dynamic.InvokeGet(myDeserializedClass.Data[i], year));
						//float.TryParse(myDeserializedClass.Data[i]._2018, out value);

						//country.GetComponent<GenerateRandomPoint>().ClearEnergyParticles();
						country.GetComponent<GenerateRandomPoint>().GeneratePoints(randomCol,year, (int)value, "energy");
					}
					catch (Exception)
					{

						throw;
					}

				}
			}
		}

	}

	void PopulatePopulation(string year)
	{
		//population
		{
			Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(populationFile.ToString());

			Debug.Log("data.count" + myDeserializedClass.Data.Count);

			for (int i = 0; i < myDeserializedClass.Data.Count; i++)
			{
				if (GameObject.Find(myDeserializedClass.Data[i].CountryName))
				{
					GameObject country = GameObject.Find(myDeserializedClass.Data[i].CountryName);
					float value = 0f;
					try
					{
						if (Dynamic.InvokeGet(myDeserializedClass.Data[i], year) != null)
						{
							float.TryParse(Dynamic.InvokeGet(myDeserializedClass.Data[i], year), out value);
						}
						//Debug.Log("dynamic test:" + Dynamic.InvokeGet(myDeserializedClass.Data[i], year));
						//float.TryParse(myDeserializedClass.Data[i]._2018, out value);

						//country.GetComponent<GenerateRandomPoint>().ClearLiteracyParticles();
						//country.GetComponent<GenerateRandomPoint>().ClearEnergyParticles();
						//country.GetComponent<GenerateRandomPoint>().ClearWageParticles();
						country.GetComponent<GenerateRandomPoint>().GeneratePoints(randomCol, year, (int)value, "literacy");
					}
					catch (Exception)
					{

						throw;
					}

				}
			}
		}

		

	}

	SimpleMeshData GenerateCountry(Country country)
	{
		SimpleMeshData countryMeshData = new SimpleMeshData(country.name);

		for (int i = 0; i < country.shape.polygons.Length; i++)
		{
			// Try get average height. Note: this data is from different source so some names might not match. (TODO: fix)
			float h = 0;
			bool a = averageCountryElevations.TryGetValue(country.name, out h);
			bool b = averageCountryElevations.TryGetValue(country.nameOfficial, out h);
			bool c = averageCountryElevations.TryGetValue(country.name_long, out h);
			h = h / maxElevation;
			float elevation = minRaiseHeight + h * raiseHeightMultiplier;
			SimpleMeshData polygonMeshData = GeneratePolygon(country.shape.polygons[i], elevation, country.name);
			if (polygonMeshData != null)
			{
				countryMeshData.Combine(polygonMeshData);
			}
		}

		countryMeshData.RecalculateNormals();
		return countryMeshData;
	}

	SimpleMeshData GeneratePolygon(Polygon polygon, float elevation, string countryName)
	{
		//DebugExtra.DrawPath(polygon.paths[0].GetPointsAsVector2(), false, Color.red, 1000);
		List<Coordinate> innerPoints = new List<Coordinate>();
		Vector2[] originalOutline = polygon.paths[0].GetPointsAsVector2(includeLastPoint: false);
		Bounds2D bounds2D = new Bounds2D(originalOutline);

		for (int i = 0; i < spherePoints2D.Length; i++)
		{
			Vector2 p = spherePoints2D[i].ToVector2();
			if (bounds2D.Contains(p))
			{
				if (Seb.Maths.PolygonContainsPoint(p, originalOutline))
				{
					innerPoints.Add(spherePoints2D[i]);
				}
			}
		}

		Polygon processedPolygon = PolygonProcessor.RemoveDuplicatesAndEdgePoints(polygon);
		(Polygon reprojectedPolygon, Coordinate[] reprojectedInnerPoints) = PolygonProcessor.Reproject(processedPolygon, innerPoints.ToArray());

		if (processedPolygon.paths[0].NumPoints > 3)
		{
			float area = Seb.Maths.PolygonArea(processedPolygon.Outline.GetPointsAsVector2());
			float elevationMultiplier = Mathf.Lerp(0.05f, 1, area / tinyAreaThreshold);
			elevation *= elevationMultiplier;

			int[] triangles = Triangulator.Triangulate(reprojectedPolygon, reprojectedInnerPoints, false);

			List<Vector3> vertices = new List<Vector3>();
			Vector3[] outlinePoints = SpherizePoints(processedPolygon.paths[0].points, radius + elevation);
			vertices.AddRange(outlinePoints);
			vertices.AddRange(SpherizePoints(innerPoints.ToArray(), radius + elevation));
			for (int i = 0; i < processedPolygon.NumHoles; i++)
			{
				vertices.AddRange(SpherizePoints(processedPolygon.Holes[i].points, radius + elevation));
			}

			// Create rim mesh
			//SimpleMeshData rim = RimMeshGenerator.GenerateOnSphere(outlinePoints, elevation + 1);
			SimpleMeshData meshData = new SimpleMeshData(vertices.ToArray(), triangles);
			//meshData.Combine(rim);

			return meshData;
		}
		return null;
	}

	Vector3[] SpherizePoints(Coordinate[] points, float radius)
	{

		Vector3[] pointsOnSphere = new Vector3[points.Length];
		for (int i = 0; i < pointsOnSphere.Length; i++)
		{
			pointsOnSphere[i] = GeoMaths.CoordinateToPoint(points[i], radius);
		}

		return pointsOnSphere;
	}

	protected override string SavePath
	{
		get
		{
			return FileHelper.MakePath("Assets", "Graphics", "Globe Map", "Meshes"); ;
		}
	}
}

[SerializeField]
public class LoadedCountryData
{
	public string countryName;
	public Dictionary<string, float> values;
}