using UnityEditor;

/// Configuracoes de importacao do campus_maua.obj (aplicadas automaticamente).
public class MapaImportSettings : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith("Assets/Mapa/") || !assetPath.EndsWith(".obj")) return;
        var imp = (ModelImporter)assetImporter;
        imp.globalScale = 1f;
        imp.useFileScale = true;
        imp.indexFormat = ModelImporterIndexFormat.UInt32;   // malhas com mais de 65k vertices
        imp.importNormals = ModelImporterNormals.Calculate;   // o OBJ nao tem normais
        imp.normalSmoothingAngle = 25f;
        imp.addCollider = true;                               // MeshCollider para poder andar no chao
        imp.materialImportMode = ModelImporterMaterialImportMode.None; // materiais criados pelo MauaVRSetup
        imp.meshCompression = ModelImporterMeshCompression.Off;
        imp.isReadable = false;
    }
}
