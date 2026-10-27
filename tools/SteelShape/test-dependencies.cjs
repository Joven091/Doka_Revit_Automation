'use strict';
const path = require('node:path');
module.exports = function testDependency(name) {
  const roots = [path.join(__dirname, 'node_modules'), path.join(__dirname, '.test-deps', 'node_modules'), path.join(__dirname, '..', 'rhs-weight-comparison', '.test-deps', 'node_modules')];
  for (const root of roots) {
    try { return require(require.resolve(name, { paths: [root] })); }
    catch (error) { if (error.code !== 'MODULE_NOT_FOUND') throw error; }
  }
  throw new Error('Install the development dependency ' + name + ' into .test-deps. The calculator HTML itself needs no dependencies.');
};
