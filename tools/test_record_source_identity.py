"""Real CLI source-archive credential regressions; no commits or real secrets."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT=Path(__file__).resolve().parents[1]
SETTINGS='QuestFlight/Assets/Resources/DevAgentSettings.asset'


class SourceIdentityCredentialTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(prefix='QuestFlight identity fixture ')
        self.addCleanup(self.tmp.cleanup)
        self.directory=Path(self.tmp.name)
        self.repo=self.directory/'selected checkout'
        subprocess.run(['git','clone','--shared','--no-checkout',str(ROOT),str(self.repo)],check=True,capture_output=True)
        self.git('read-tree','HEAD')
        # Keep missing product payloads outside this fixture's diff. Only the
        # credential file and our diagnostic text file participate; user Git is
        # untouched and no commit is created.
        names=self.git('ls-files','-z')
        self.git('update-index','--skip-worktree','-z','--stdin',data=names)
        self.git('update-index','--no-skip-worktree',SETTINGS)
        self.settings=self.repo/SETTINGS
        self.settings.parent.mkdir(parents=True)
        (self.repo/'DIAGNOSTIC_SOURCE.txt').write_text('known safe current source\n')

    def git(self,*args,data=None):
        return subprocess.check_output(['git','-C',str(self.repo),*args],input=data,stderr=subprocess.DEVNULL)

    def invoke(self,output):
        return subprocess.run([sys.executable,str(ROOT/'tools/record_source_identity.py'),'--root',str(self.repo),'--output',str(output)],capture_output=True,text=True)

    def test_empty_token_with_next_property_passes_actual_cli(self):
        for index,value in enumerate(('', '""', "''", '# explicitly empty', 'null')):
            with self.subTest(value=value):
                content=f'%YAML 1.1\nMonoBehaviour:\n  accessToken: {value}\n  serverAddress: preserved-fixture-address\n  witToken:\n  enableWit: 0\n'
                self.settings.write_text(content)
                output=self.directory/f'output {index}'
                result=self.invoke(output)
                self.assertEqual(0,result.returncode,result.stderr)
                identity=json.loads((output/'source_identity.json').read_text())
                row=next(r for r in identity['changes'] if r['path']==SETTINGS)
                self.assertEqual(hashlib.sha256(self.settings.read_bytes()).hexdigest(),row['sha256'])
                with zipfile.ZipFile(output/'source_changes.zip') as archive:
                    self.assertEqual(self.settings.read_bytes(),archive.read(SETTINGS))
                patch=(output/'source_changes.patch').read_bytes()
                self.assertNotIn(SETTINGS.encode(),patch)
                self.assertNotIn(b'accessToken:',patch)

    def test_populated_credentials_reject_before_any_artifact_or_value_output(self):
        marker='DIAGNOSTIC-SECRET-MUST-NOT-LEAK'
        for index,key in enumerate(('accessToken','witToken','password','apiKey','clientSecret')):
            with self.subTest(key=key):
                self.settings.write_text(f'MonoBehaviour:\n  accessToken:\n  serverAddress: fixture\n  {key}: {marker}\n')
                output=self.directory/f'rejected {index}'
                result=self.invoke(output)
                self.assertNotEqual(0,result.returncode)
                self.assertNotIn(marker,result.stdout+result.stderr)
                self.assertFalse(output.exists(),'Rejected evidence must not leave partial patch/archive files')

    def test_staged_previous_secret_never_enters_patch_or_current_archive(self):
        previous='DIAGNOSTIC-ORIGINAL-SECRET-NEVER-SHIPPED'
        self.settings.write_text(f'MonoBehaviour:\n  accessToken: {previous}\n  serverAddress: staged-fixture\n')
        self.git('add',SETTINGS)
        self.settings.write_text('MonoBehaviour:\n  accessToken:\n  serverAddress: current-preserved-fixture\n  witToken:\n  enableWit: 0\n')
        output=self.directory/'staged and current'
        result=self.invoke(output)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertNotIn(previous.encode(),(output/'source_changes.patch').read_bytes())
        with zipfile.ZipFile(output/'source_changes.zip') as archive:
            self.assertEqual(self.settings.read_bytes(),archive.read(SETTINGS))
            self.assertNotIn(previous.encode(),archive.read(SETTINGS))

    def test_deleted_settings_are_excluded_from_patch(self):
        output=self.directory/'deleted settings'
        result=self.invoke(output)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertNotIn(SETTINGS.encode(),(output/'source_changes.patch').read_bytes())
        identity=json.loads((output/'source_identity.json').read_text())
        row=next(r for r in identity['changes'] if r['path']==SETTINGS)
        self.assertTrue(row['deleted'])

    def test_ignored_authored_same_size_mutation_changes_identity_but_cache_does_not(self):
        self.settings.write_text('MonoBehaviour:\n  accessToken:\n  serverAddress: fixture\n')
        (self.repo/'.git/info/exclude').write_text('QuestFlight/Assets/StreamingAssets/**\nQuestFlight/Assets/Generated/**\nQuestFlight/Library/**\n')
        relative='QuestFlight/Assets/StreamingAssets/QuestFlightAerialClipmap/fixture.bytes'
        payload=self.repo/relative
        payload.parent.mkdir(parents=True)
        payload.write_bytes(b'known-payload-A')
        def record(label):
            output=self.directory/label
            result=self.invoke(output)
            self.assertEqual(0,result.returncode,result.stderr)
            return output,json.loads((output/'source_identity.json').read_text())
        first,a=record('ignored initial')
        row=next(r for r in a['ignoredAuthoredInputs'] if r['path']==relative)
        self.assertEqual(hashlib.sha256(payload.read_bytes()).hexdigest(),row['sha256'])
        with zipfile.ZipFile(first/'source_changes.zip') as archive:
            self.assertNotIn(relative,archive.namelist(),'Ignored binary inputs are hash-only, not archived')
        cache=self.repo/'QuestFlight/Library/import.cache'
        cache.parent.mkdir(parents=True)
        cache.write_bytes(b'unrelated generated Library bytes')
        _,b=record('ignored same payload changed cache')
        self.assertEqual(a,b,'Unrelated ignored Library cache changes source identity')
        payload.write_bytes(b'known-payload-B')
        _,c=record('ignored mutated same size payload')
        self.assertEqual(a['changes'],c['changes'])
        self.assertNotEqual(a['ignoredAuthoredInputs'],c['ignoredAuthoredInputs'])

    def test_temp_private_and_raw_files_do_not_enter_ignored_shipping_identity(self):
        self.settings.write_text('MonoBehaviour:\n  accessToken:\n  serverAddress: fixture\n')
        (self.repo/'.git/info/exclude').write_text('QuestFlight/Assets/StreamingAssets/**\nQuestFlight/Assets/__ReservoirWatercraftTemp/**\n')
        for name in ('QuestFlight/Assets/StreamingAssets/QuestFlightAerialClipmap/Restricted/private.asset','QuestFlight/Assets/StreamingAssets/QuestFlightAerialClipmap/__Diagnostic/temp.asset','QuestFlight/Assets/StreamingAssets/QuestFlightAerialClipmap/raw.zip','QuestFlight/Assets/__ReservoirWatercraftTemp/private.fbx'):
            path=self.repo/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'nonshipping diagnostic bytes')
        output=self.directory/'excluded payloads'
        result=self.invoke(output)
        self.assertEqual(0,result.returncode,result.stderr)
        identity=json.loads((output/'source_identity.json').read_text())
        self.assertEqual([],identity['ignoredAuthoredInputs'])

if __name__=='__main__':unittest.main()
