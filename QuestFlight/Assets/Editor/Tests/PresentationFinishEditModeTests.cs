#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using NUnit.Framework;
using QuestFlight.Product;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class PresentationFinishEditModeTests
    {
        [Test] public void CaptionClockHandlesHoldAndRepeat()
        {
            var clip=new ProductCaptionTimeline.Clip{phrases=new[]{new ProductCaptionTimeline.Phrase{start=.1f,end=1,text="First"},new ProductCaptionTimeline.Phrase{start=1.5f,end=2,text="Second"}}};
            Assert.That(ProductCaptionTimeline.PhraseAt(clip,1.4f),Is.EqualTo("First"));
            Assert.That(ProductCaptionTimeline.PhraseAt(clip,1.5f),Is.EqualTo("Second"));
            Assert.That(ProductCaptionTimeline.PhraseAt(clip,30),Is.EqualTo("Second"));
            Assert.That(ProductCaptionTimeline.PhraseAt(clip,0),Is.EqualTo("First"));
        }
        [Test] public void SaveRecoversPreviousCompleteRecordAfterCorruption()
        {
            string dir=Path.Combine(Path.GetTempPath(),"QflSaveTest_"+Guid.NewGuid());Directory.CreateDirectory(dir);string file=Path.Combine(dir,"record.json");
            try
            {
                ProductLocalSave.Write(file,"{\"seconds\":42}");ProductLocalSave.Write(file,"{\"seconds\":55}");File.WriteAllText(file,"not json");
                var result=ProductLocalSave.Read(file,text=>JsonUtility.FromJson<ProductFlightExperience.Record>(text),out bool recovered);
                Assert.That(recovered,Is.True);Assert.That(result.seconds,Is.EqualTo(42));
            }
            finally {Directory.Delete(dir,true);}
        }
        [Test] public void CaptionSizeDoesNotChangeMenuSize()
        {
            string key="QuestFlight.CaptionScale",menuKey="QuestFlight.TextScale";bool had=PlayerPrefs.HasKey(key),hadMenu=PlayerPrefs.HasKey(menuKey);float previous=PlayerPrefs.GetFloat(key),menu=PlayerPrefs.GetFloat(menuKey,1);
            try{PlayerPrefs.DeleteKey(key);ProductComfortPreferences.Set("TextScale",1.2f);Assert.That(ProductComfortPreferences.CaptionScale,Is.EqualTo(1));ProductComfortPreferences.Set("CaptionScale",1.6f);Assert.That(ProductComfortPreferences.CaptionScale,Is.EqualTo(1.6f));Assert.That(ProductComfortPreferences.TextScale,Is.EqualTo(1.2f));}
            finally{if(had)PlayerPrefs.SetFloat(key,previous);else PlayerPrefs.DeleteKey(key);if(hadMenu)PlayerPrefs.SetFloat(menuKey,menu);else PlayerPrefs.DeleteKey(menuKey);}
        }
    }
}
#endif
