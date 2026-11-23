import json
import pathlib
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[1]
def source(name): return (ROOT/'Source'/name).read_text(encoding='utf-8')

class NaturalVillagesV09Tests(unittest.TestCase):
 def test_metadata(self):
  m=json.loads((ROOT/'modinfo.json').read_text())
  self.assertEqual(m['Version'],'0.9.0')
  self.assertEqual(m['PackageName'],'natural.villages.medieval')
 def test_real_door_block(self):
  s=source('MedievalHouseGenerator.cs')
  self.assertIn('"WoodenDoorBlock"',s)
  self.assertIn('p.Door',s)
 def test_framed_glass(self):
  s=source('MedievalHouseGenerator.cs')
  self.assertIn('"FramedGlassBlock"',s)
  self.assertIn('p.Glass',s)
 def test_tree_clear_limited(self):
  s=source('VillageTerrainHelper.cs')
  self.assertIn('maxX>15',s)
  self.assertIn('maxZ>15',s)
  self.assertIn('minY<1',s)
 def test_ground_preserves_water(self):
  s=source('VillageTerrainHelper.cs')
  self.assertIn('return IsGround(name)?y:-1',s)
  self.assertNotIn('WaterBlock"',s)
 def test_vegetation_matches_canopy(self):
  s=source('VillageTerrainHelper.cs')
  self.assertIn('name.IndexOf("Leaves"',s)
  self.assertIn('name.EndsWith("WoodBlock"',s)
 def test_various_site_candidates(self):
  s=source('MedievalHouseGenerator.cs')
  self.assertIn('new[]{4,3,5,2,6,7}',s)
  self.assertIn('new[]{4,5,6}',s)
  self.assertIn('MaxGradeDifference=8',s)
 def test_house_writes_only_this_chunk(self):
  s=source('MedievalHouseGenerator.cs')
  self.assertIn('x>15',s)
  self.assertIn('SetCellValueFast(',s)
  self.assertNotIn('ChangeCell(',s)
 def test_well_uses_vegetation_survey(self):
  s=source('MedievalWellGenerator.cs')
  self.assertIn('VillageTerrainHelper.GroundY',s)
  self.assertIn('ClearVegetation(',s)
 def test_roads_preserve_unknown_blocks(self):
  s=source('VillageRoadGenerator.cs')
  self.assertIn('VillageTerrainHelper.CanClear',s)
  self.assertIn('VillageTerrainHelper.GroundY',s)
 def test_roads_log_steep_transitions(self):
  s=source('VillageRoadGenerator.cs')
  self.assertIn('steepEdges',s)
  self.assertNotIn('if(steep)continue;',s)
 def test_existing_terrain_gen_hook(self):
  s=source('NaturalVillagesLoader.cs')
  self.assertIn('TerrainContentsGenerator24Initialize(',s)
  self.assertIn('ITerrainContentsGenerator',s)
 def test_seed_registry_persistent(self):
  s=source('WorldSeedResolver.cs')
  self.assertIn('TryRead(',s)
 def test_one_chunk_per_structure(self):
  s=source('VillageChunkSelector.cs')
  self.assertIn('ChunkX',s)
  self.assertIn('ChunkZ',s)
 def test_nominal_spacing(self):
  s=source('SeededVillagePlanner.cs')
  self.assertIn('CellSize = 400',s)

if __name__=='__main__': unittest.main()
