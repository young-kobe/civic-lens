import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TARGET = 'b' * 40
PREVIOUS = 'a' * 40


class DeployUpdateTests(unittest.TestCase):
    def run_update(self, status='', status_exit=0):
        with tempfile.TemporaryDirectory(prefix='civic deploy ') as directory:
            root = Path(directory)
            scripts = root / 'repository/deploy/scripts'
            scripts.mkdir(parents=True)
            for name in ('update.sh', 'common.sh'):
                shutil.copyfile(ROOT / 'deploy/scripts' / name, scripts / name)
            tunnel = scripts.parent / 'cloudflared/config.yml'
            tunnel.parent.mkdir()
            tunnel.write_text('service: http://127.0.0.1:8080\n')
            runtime = root / 'runtime'
            tag_file = runtime / 'env/compose.env'
            tag_file.parent.mkdir(parents=True)
            original = f'CIVIC_LENS_IMAGE_TAG={PREVIOUS}\nCIVIC_LENS_CADDY_PORT=8080\n'
            tag_file.write_text(original)
            binaries = root / 'bin'
            binaries.mkdir()
            git = binaries / 'git'
            git.write_text('''#!/bin/bash
case "$*" in
  *"rev-parse HEAD") printf '%s\\n' "$CIVIC_TEST_CHECKOUT" ;;
  *"status --porcelain -- deploy")
    printf '%s' "$CIVIC_TEST_STATUS"
    exit "$CIVIC_TEST_STATUS_EXIT" ;;
  *) exit 99 ;;
esac
''')
            docker = binaries / 'docker'
            docker.write_text('''#!/bin/bash
printf '%s\\n' "$*" >> "$CIVIC_TEST_DOCKER_CALLS"
''')
            git.chmod(0o700)
            docker.chmod(0o700)
            calls = root / 'docker-calls'
            env = dict(os.environ, PATH=str(binaries) + os.pathsep + os.environ['PATH'],
                       CIVIC_LENS_ROOT=str(runtime), CIVIC_TEST_CHECKOUT=TARGET,
                       CIVIC_TEST_STATUS=status, CIVIC_TEST_STATUS_EXIT=str(status_exit),
                       CIVIC_TEST_DOCKER_CALLS=str(calls))
            env.pop('BASH_ENV', None)
            result = subprocess.run(['bash', '--noprofile', '--norc', str(scripts / 'update.sh'), TARGET],
                                    env=env, capture_output=True, text=True, timeout=10)
            return result, tag_file.read_text(), calls.read_text() if calls.exists() else '', original

    def test_git_status_failure_stops_before_compose_or_tag_changes(self):
        result, tag, calls, original = self.run_update(status_exit=73)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(original, tag)
        self.assertEqual('', calls)

    def test_dirty_deploy_files_stop_before_compose_or_tag_changes(self):
        result, tag, calls, original = self.run_update(status=' M deploy/caddy/Caddyfile\n')
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(original, tag)
        self.assertEqual('', calls)

    def test_clean_deploy_files_allow_the_update(self):
        result, tag, calls, _ = self.run_update()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(f'CIVIC_LENS_IMAGE_TAG={TARGET}\n', tag)
        self.assertIn(' pull\n', calls)


if __name__ == '__main__':
    unittest.main()
