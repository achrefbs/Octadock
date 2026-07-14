(function () {
  'use strict';

  // Browsers block fetch() from file:// pages. Load the generated base64 copy
  // synchronously only for direct-file previews; hosted pages keep using the
  // smaller binary GLB request.
  if (location.protocol === 'file:') {
    document.write('<script src="assets/models/octopus-fable-data.js"><\/script>');
  }
})();
