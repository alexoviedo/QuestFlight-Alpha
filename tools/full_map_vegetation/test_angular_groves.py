import unittest
import numpy as np
from build_angular_groves import pack_masks,RECORD

class MaskContractTests(unittest.TestCase):
    def test_native_corners_and_page_cells(self):
        mask=np.zeros((2016,2016),bool)
        mask[0,0]=mask[7,7]=mask[0,8]=mask[8,0]=True
        packed=pack_masks(mask)
        self.assertEqual(packed[0].tolist(),[1,0,0,32768])
        self.assertEqual(int(packed[1,0]),1);self.assertEqual(int(packed[252,0]),1)
        self.assertEqual(int(np.count_nonzero(packed)),4)
    def test_excluded_page_stays_empty(self):
        self.assertFalse(pack_masks(np.zeros((2016,2016),bool)).any())
    def test_full_coverage_words_are_float_exact(self):
        words=pack_masks(np.ones((2016,2016),bool))
        self.assertTrue((words==65535).all())
        self.assertTrue(np.array_equal(words,words.astype(np.float32).astype(np.uint16)))
    def test_packed_runtime_layout(self):
        self.assertEqual(RECORD.itemsize,18)
        self.assertEqual(RECORD.fields['middle'][1],2)
        self.assertEqual(RECORD.fields['depth'][1],6)
        self.assertEqual(RECORD.fields['mask'][1],10)
if __name__=='__main__':unittest.main()
