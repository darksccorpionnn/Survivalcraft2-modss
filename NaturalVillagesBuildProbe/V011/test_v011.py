"""Offline/source regression tests; these are not Android gameplay tests."""
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
def src(name):return (ROOT/'Source'/name).read_text(encoding='utf-8')
def accepts(ground,width,depth,xrange,zrange,max_grade):
 for x in xrange:
  for z in zrange:
   vals=[ground[i][j] for i in range(x,x+width) for j in range(z,z+depth)]
   if min(vals)>=3 and max(vals)-min(vals)<=max_grade:return True
 return False
class V11Tests(unittest.TestCase):
 def test_version(self):
  self.assertEqual(json.loads((ROOT/'modinfo.json').read_text())['Version'],'0.11.0')
 def test_target_count(self):
  s=src('SeededVillagePlanner.cs')
  self.assertIn('return 10 + (int)',s)
  self.assertIn('% 3UL',s)
 def test_more_plot_candidates(self):
  s=src('MedievalHouseGenerator.cs')
  self.assertIn('for(int sx=1;sx<=8;sx++)',s)
  self.assertIn('for(int sz=4;sz<=6;sz++)',s)
  self.assertIn('MaxGradeDifference=18',s)
  self.assertIn('grade=high-Math.Min(4,(high-low)/3)',s)
 def test_fallback(self):
  s=src('MedievalHouseGenerator.cs')
  self.assertIn('MedievalHillsideHutGenerator.TryGenerate(chunk, slot.Index)',s)
  self.assertIn('both houses rejected',s)
 def test_chunk_safety(self):
  s=src('MedievalHillsideHutGenerator.cs')
  for tok in ('for(int sx=1;sx<=10;sx++)','for(int sz=5;sz<=10;sz++)',
   'MaxGradeDifference = 24','x>15||z<0||z>15','if(h<3)',
   'WoodenDoorBlock','FramedGlassBlock','WickerLampBlock','SetCellValueFast'):
   self.assertIn(tok,s)
 def test_synthetic_hill_fallback(self):
  ramp=[[60+round(i*1.75+j*.7) for j in range(16)] for i in range(16)]
  self.assertFalse(accepts(ramp,7,9,(4,3,5,2,6),(4,5),8))
  self.assertTrue(accepts(ramp,5,5,range(1,11),range(5,11),24))
 def test_water_rejected(self):
  wet=[[-1]*16 for _ in range(16)]
  self.assertFalse(accepts(wet,7,9,range(1,9),range(4,7),18))
  self.assertFalse(accepts(wet,5,5,range(1,11),range(5,11),24))
 def test_wicker_and_stone(self):
  self.assertIn('WickerLampBlock',src('MedievalHouseGenerator.cs'))
  self.assertIn('WickerLampBlock',src('MedievalHillsideHutGenerator.cs'))
  self.assertIn('CobblestoneBlock',src('VillageRoadGenerator.cs'))
 def test_packaging(self):
  s=(ROOT/'package_mod.py').read_text()
  self.assertIn('V0.11.scmod',s)
  self.assertIn('if not DLL.is_file()',s)
 def test_independent(self):
  self.assertNotIn('LivingTowns','\n'.join(p.read_text() for p in (ROOT/'Source').glob('*.cs')))
